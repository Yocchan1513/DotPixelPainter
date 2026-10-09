using System.Numerics;
using DotPixelPainter.Core;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;
using Windows.UI;

namespace DotPixelPainter;

/// <summary>
/// Minecraft スキンモード: キャンバスの部位ガイドと、プレビューの 3D 表示。
/// 3D は面ごとに絵を平行四辺形に変形して奥から順に重ねる（正射影なので、2D の変形だけで正確に描ける）。
/// </summary>
public sealed partial class MainWindow
{
    private const float DefaultYaw = -0.55f;
    private const float DefaultPitch = 0.3f;
    private const double NormalPreviewHeight = 140;
    private const double SkinPreviewHeight = 260;

    private static readonly Color GuideColor = Color.FromArgb(150, 0, 190, 255);
    private static readonly Color OuterGuideColor = Color.FromArgb(150, 255, 140, 0);
    private static readonly Color HoverColor = Color.FromArgb(60, 255, 230, 0);

    private float _skinYaw = DefaultYaw;
    private float _skinPitch = DefaultPitch;
    private Point? _skinDragFrom;
    private (SkinBox Box, SkinFace Face)? _skinHover;

    private bool IsSkinTab(DocumentTab? tab) => tab is { Document.SkinMode: true };

    // ---- 切り替え ----

    private void SkinToggle_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentTab is not { } tab)
        {
            return;
        }

        bool on = SkinToggle.IsChecked == true;
        if (on && !tab.Document.SkinMode)
        {
            // 初めてスキンとして見るときは、絵から腕の太さを推測する
            tab.Document.SlimArms = SkinLayout.GuessSlim(tab.Document.Composite());
        }

        tab.Document.SkinMode = on;
        UpdateSkinUi();
    }

    private void SkinOption_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentTab is { } tab)
        {
            tab.Document.SlimArms = SlimToggle.IsChecked == true;
        }

        UpdateSkinUi();
    }

    /// <summary>今のタブに合わせて、スキン関係のボタンとプレビューの表示を切り替える。</summary>
    private void UpdateSkinUi()
    {
        DocumentTab? tab = CurrentTab;
        bool canBeSkin = tab is not null && SkinLayout.IsSkinSize(tab.Document.Width, tab.Document.Height);
        bool skin = IsSkinTab(tab) && canBeSkin;

        SkinToggle.IsEnabled = canBeSkin;
        SkinToggle.IsChecked = skin;
        ToolTipService.SetToolTip(SkinToggle, canBeSkin
            ? "Minecraft スキンとして編集（部位ガイドと 3D プレビュー）"
            : "64×64 か 64×32 の画像で使えます");
        SkinOptions.Visibility = skin ? Visibility.Visible : Visibility.Collapsed;
        SlimToggle.IsChecked = tab?.Document.SlimArms == true;
        PreviewTitle.Text = skin ? "3D プレビュー" : "プレビュー";
        ToolTipService.SetToolTip(PreviewCanvas, skin ? "ドラッグで回転、ダブルクリックで元の向き" : "等倍と2倍（タイル表示では 3×3 に並べる）");
        PreviewCanvas.Height = skin ? SkinPreviewHeight : TileToggle.IsChecked == true ? TilePreviewHeight : NormalPreviewHeight;
        _skinHover = null;
        Canvas.Invalidate();
        PreviewCanvas.Invalidate();
        UpdateStatus(null);
    }

    // ---- キャンバスの部位ガイド ----

    private void DrawSkinGuide(CanvasDrawingSession ds, DocumentTab tab, Rect imageRect, float cell, float stroke)
    {
        if (!IsSkinTab(tab))
        {
            return;
        }

        bool slim = tab.Document.SlimArms;
        bool legacy = tab.Document.IsLegacySkin;
        foreach (SkinBox box in SkinLayout.Boxes(slim, legacy))
        {
            if (box.Mirror)
            {
                continue;
            }

            foreach (SkinFace face in Enum.GetValues<SkinFace>())
            {
                PixelRect r = SkinLayout.FaceRect(box, face);
                int x = IsFlipped ? tab.Document.Width - r.Right : r.X;
                var rect = new Rect(
                    imageRect.X + x * cell + stroke / 2,
                    imageRect.Y + r.Y * cell + stroke / 2,
                    r.Width * cell - stroke,
                    r.Height * cell - stroke);
                if (_skinHover is { } hover && hover.Box == box && hover.Face == face)
                {
                    ds.FillRectangle(rect, HoverColor);
                }

                ds.DrawRectangle(rect, box.Outer ? OuterGuideColor : GuideColor, stroke);
            }
        }
    }

    /// <summary>カーソルの下の部位を覚える（ガイドの強調とステータスバー用）。</summary>
    private void UpdateSkinHover(DocumentTab tab, (int X, int Y) p)
    {
        if (!IsSkinTab(tab))
        {
            return;
        }

        var hit = SkinLayout.HitTest(p.X, p.Y, tab.Document.SlimArms, tab.Document.IsLegacySkin);
        if (hit != _skinHover)
        {
            _skinHover = hit;
            Canvas.Invalidate();
        }
    }

    private string SkinStatus() => _skinHover is { } h
        ? $"{SkinLayout.PartName(h.Box.Part)}{(h.Box.Outer ? "（外側）" : "")}・{SkinLayout.FaceName(h.Face)}"
        : "";

    // ---- 3D プレビュー ----

    private void DrawSkin3D(CanvasControl sender, CanvasDrawingSession ds, CanvasBitmap bitmap, PixelDocument document)
    {
        float width = (float)sender.ActualWidth;
        float height = (float)sender.ActualHeight;
        float scale = Math.Min(width, height) * 0.9f / 34f; // 人形の高さは外側込みで約 33px
        var center = new Vector2(width / 2, height / 2);
        Matrix4x4 rotation = Matrix4x4.CreateRotationY(_skinYaw) * Matrix4x4.CreateRotationX(_skinPitch);
        var pivot = new Vector3(0, 16, 0);

        Vector3 Project3(Vector3 p) => Vector3.Transform(p - pivot, rotation);
        Vector2 ToScreen(Vector3 q) => new(center.X + q.X * scale, center.Y - q.Y * scale);

        var faces = new List<(float Depth, SkinFaceQuad Quad, Vector2 O, Vector2 A, Vector2 B, float Shade)>();
        foreach (SkinFaceQuad quad in SkinLayout.Quads(document.SlimArms, document.IsLegacySkin, OuterToggle.IsChecked == true))
        {
            Vector3 normal = Vector3.TransformNormal(quad.Normal, rotation);
            if (normal.Z <= 0.001f)
            {
                continue; // 裏を向いている面は見えない
            }

            Vector3 o = Project3(quad.Origin);
            Vector2 o2 = ToScreen(o);
            Vector2 a2 = ToScreen(Project3(quad.Origin + quad.Across)) - o2;
            Vector2 b2 = ToScreen(Project3(quad.Origin + quad.Down)) - o2;
            float depth = Project3(quad.Origin + (quad.Across + quad.Down) / 2).Z;

            // 光が正面やや上から当たる想定で、横や下を向いた面を少し暗くする（外側の層は透明部分があるので暗くしない）
            float light = Math.Clamp(0.55f + 0.35f * normal.Z + 0.25f * normal.Y, 0.5f, 1f);
            faces.Add((depth, quad, o2, a2, b2, quad.Box.Outer ? 0 : 1 - light));
        }

        ds.Antialiasing = CanvasAntialiasing.Aliased;
        foreach (var (_, quad, o2, a2, b2, shade) in faces.OrderBy(f => f.Depth))
        {
            PixelRect src = quad.Source;
            ds.Transform = new Matrix3x2(a2.X / src.Width, a2.Y / src.Width, b2.X / src.Height, b2.Y / src.Height, o2.X, o2.Y);

            // 面どうしのすき間が見えないよう、ほんの少し大きく描く
            var dest = new Rect(-0.03, -0.03, src.Width + 0.06, src.Height + 0.06);
            ds.DrawImage(bitmap, dest, new Rect(src.X, src.Y, src.Width, src.Height), 1f, CanvasImageInterpolation.NearestNeighbor);
            if (shade > 0.01f)
            {
                ds.FillRectangle(new Rect(0, 0, src.Width, src.Height), Color.FromArgb((byte)(shade * 255), 0, 0, 0));
            }
        }

        ds.Transform = Matrix3x2.Identity;
    }

    private void PreviewCanvas_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (IsSkinTab(CurrentTab))
        {
            _skinDragFrom = e.GetCurrentPoint(PreviewCanvas).Position;
            PreviewCanvas.CapturePointer(e.Pointer);
            e.Handled = true;
        }
    }

    private void PreviewCanvas_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_skinDragFrom is not { } from)
        {
            return;
        }

        Point now = e.GetCurrentPoint(PreviewCanvas).Position;
        _skinYaw += (float)(now.X - from.X) * 0.012f;
        _skinPitch = Math.Clamp(_skinPitch + (float)(now.Y - from.Y) * 0.012f, -1.4f, 1.4f);
        _skinDragFrom = now;
        PreviewCanvas.Invalidate();
    }

    private void PreviewCanvas_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _skinDragFrom = null;
        PreviewCanvas.ReleasePointerCapture(e.Pointer);
    }

    /// <summary>ダブルクリックで斜め前からの向きに戻す。</summary>
    private void PreviewCanvas_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        _skinYaw = DefaultYaw;
        _skinPitch = DefaultPitch;
        PreviewCanvas.Invalidate();
    }
}
