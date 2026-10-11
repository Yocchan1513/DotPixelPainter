namespace DotPixelPainter;

/// <summary>
/// MainWindow.xaml の文字（{x:Bind local:Texts.名前} で使う）。日本語と英語を並べて書く。
/// 言語は起動の最初に決まる（Lang.Japanese）ので、画面を作るときには選ばれている。
/// </summary>
internal static class Texts
{
    public static string AltPickHint => T("Alt+クリックでスポイト", "Alt+click to pick a color");

    public static string BackColorTip => T("背景色（クリックで描画色と入れ替え X / 白黒に戻す D）", "Background color (click to swap with foreground: X / reset to black and white: D)");

    public static string Brush1px => T("ブラシ 1px", "Brush 1px");

    public static string BrushSizeTip => T("ペン先の大きさ（[ と ] でも変えられる）", "Brush size (also [ and ])");

    public static string Color => T("カラー", "Color");

    public static string ColorMask => T("カラーマスク", "Color mask");

    public static string ColorModeTip => T("スライダーを RGB と HSV で切り替え", "Switch the sliders between RGB and HSV");

    public static string Delete => T("削除", "Delete");

    public static string ForeColorTip => T("描画色（クリックでカラーピッカー）", "Foreground color (click for the color picker)");

    public static string Grid => T("グリッド", "Grid");

    public static string GridTip => T("1pxグリッド線（4倍以上で表示）", "1px grid lines (shown at 4x or more)");

    public static string HexTip => T("色コード（RRGGBB または AARRGGBB）。Enter で反映", "Color code (RRGGBB or AARRGGBB). Press Enter to apply");

    public static string HueShiftTip => T("上の段で、影を青寄り・ハイライトを黄寄りに色相をずらす", "In the top row, shift shadows toward blue and highlights toward yellow");

    public static string LayerAddTip => T("新しいレイヤーを上に追加", "Add a new layer above");

    public static string LayerDownTip => T("ひとつ下へ", "Move down");

    public static string LayerDuplicateTip => T("複製", "Duplicate");

    public static string LayerMergeTip => T("下のレイヤーと結合", "Merge down");

    public static string LayerUpTip => T("ひとつ上へ", "Move up");

    public static string Layers => T("レイヤー", "Layers");

    public static string MaskAddTip => T("描画色をマスクの色に追加（8色まで。色をクリックで外す）", "Add the foreground color to the mask (up to 8; click a color to remove it)");

    public static string MaskClearTip => T("マスクの色をすべて外す", "Remove all mask colors");

    public static string MaskOnly => T("指定色だけ", "Only these colors");

    public static string MaskProtect => T("指定色を保護", "Protect colors");

    public static string MenuEdit => T("編集(E)", "Edit");

    public static string MenuFile => T("ファイル(F)", "File");

    public static string MenuHelp => T("ヘルプ(H)", "Help");

    public static string MenuImage => T("画像(I)", "Image");

    public static string MenuLayer => T("レイヤー(L)", "Layer");

    public static string MenuSelect => T("選択範囲(S)", "Select");

    public static string MenuView => T("表示(V)", "View");

    public static string MergeShort => T("結", "M");

    public static string MirrorView => T("左右反転表示", "Mirror view");

    public static string MirrorViewTip => T("表示だけを左右反転（絵は変わらない）", "Mirror the view only (the image does not change)");

    public static string New => T("新規", "New");

    public static string NewTip => T("新規 (Ctrl+N)", "New (Ctrl+N)");

    public static string None => T("なし", "None");

    public static string Off => T("オフ", "Off");

    public static string Opacity100 => T("不透明度 100%", "Opacity 100%");

    public static string Open => T("開く", "Open");

    public static string OpenTip => T("開く (Ctrl+O)", "Open (Ctrl+O)");

    public static string Outer => T("外側", "Outer");

    public static string OuterTip => T("外側のレイヤー（帽子・上着・袖・ズボン）を表示", "Show the outer layer (hat, jacket, sleeves, pants)");

    public static string Palette => T("パレット", "Palette");

    public static string PaletteAddTip => T("描画色をパレットに追加", "Add the foreground color to the palette");

    public static string PaletteExport => T("GIMPパレット（.gpl）に書き出す", "Save as a GIMP palette (.gpl)");

    public static string PaletteFromImage => T("絵で使っている色を取り込む", "Add colors used in the image");

    public static string PaletteImport => T("GIMPパレット（.gpl）を読み込む", "Load a GIMP palette (.gpl)");

    public static string PaletteMenuTip => T("パレットの読み込み・書き出し", "Load and save palettes");

    public static string PaletteReset => T("初期パレットに戻す", "Reset to the default palette");

    public static string Pattern => T("パターン", "Pattern");

    public static string PatternGap => T("すき間を背景色で塗る", "Fill gaps with background color");

    public static string PatternTip => T("ペン・図形・塗りつぶしを網かけの模様で塗る", "Paint the pen, shapes and fill with a dither pattern");

    public static string PixelPerfect => T("1pxの線の角を整える", "Clean 1px line corners");

    public static string PixelPerfectTip => T("ピクセルパーフェクト: 1pxの自由線で、角にできる余分な1ドット（L字の真ん中）を描きながら消す", "Pixel perfect: while drawing 1px freehand lines, removes the extra pixel in L-shaped corners");

    public static string Preview => T("プレビュー", "Preview");

    public static string PreviewBackgroundTip => T("プレビューの背景（市松模様・白・黒・灰色・背景色）", "Preview background (checkerboard, white, black, gray, background color)");

    public static string PreviewGripTip => T("上下にドラッグで高さを変える（ダブルクリックで元に戻す）", "Drag up or down to change the height (double-click to reset)");

    public static string Redo => T("やり直し", "Redo");

    public static string RedoTip => T("やり直し (Ctrl+Y / Ctrl+Shift+Z)", "Redo (Ctrl+Y / Ctrl+Shift+Z)");

    public static string RightPanel => T("右のパネル", "Right panel");

    public static string RightPanelTip => T("右のパネルを表示する（ウィンドウが狭いときは自動でたたむ）", "Show the right panel (folds automatically when the window is narrow)");

    public static string RoundTipTip => T("丸いペン先", "Round brush");

    public static string Save => T("保存", "Save");

    public static string SaveAs => T("別名で保存", "Save as");

    public static string SaveAsTip => T("別名で保存：.dotpix（レイヤーを残す）か PNG（1枚にまとめる） (Ctrl+Shift+S)", "Save as .dotpix (keeps layers) or PNG (flattened) (Ctrl+Shift+S)");

    public static string SaveTip => T("上書き保存 (Ctrl+S)", "Save (Ctrl+S)");

    public static string ShadeBaseTip => T("今の描画色を基準にする", "Use the current foreground color as the base");

    public static string ShadeCanvasTip => T("クリックで描画色に、右クリックでパレットに追加。右の帯で基準の色相を変える", "Click to set the foreground color, right-click to add to the palette. Use the strip on the right to change the base hue");

    public static string Shades => T("色の段階", "Shades");

    public static string Skin => T("スキン", "Skin");

    public static string SkinTip => T("Minecraft スキンとして扱う（64×64・64×32 のとき）", "Treat as a Minecraft skin (64×64 or 64×32)");

    public static string SlimArms => T("スリム腕", "Slim arms");

    public static string SlimArmsTip => T("腕の太さ: オンでスリム（3px）、オフでクラシック（4px）", "Arm width: on = slim (3px), off = classic (4px)");

    public static string SquareTipTip => T("四角いペン先", "Square brush");

    public static string TileTip => T("タイル表示（3×3 に並べて継ぎ目を確かめる）", "Tiled view (3×3, to check seams)");

    public static string Undo => T("元に戻す", "Undo");

    public static string UndoTip => T("元に戻す (Ctrl+Z)", "Undo (Ctrl+Z)");

    public static string ZoomIn => T("拡大", "Zoom in");

    public static string ZoomOut => T("縮小", "Zoom out");

    public static string SplitView => T("画面を分割", "Split view");

    public static string SplitViewTip => T("画面を分割して、右に資料（別のタブや画像）を表示する", "Split the view and show a reference (another tab or an image) on the right");

    public static string RefChoose => T("表示するものを選ぶ", "Choose what to show");

    public static string RefSourceTip => T("右に表示するもの（開いているタブ・資料として開いた画像）", "What to show on the right (open tabs or reference images)");

    public static string RefOpenTip => T("資料の画像を開く（タブには加えない）", "Open a reference image (not added as a tab)");

    public static string RefCloseTip => T("分割をやめる", "Close the split view");

    public static string RefCanvasTip => T("ホイールで拡大縮小、ドラッグで移動、クリックで色を拾う、ダブルクリックで全体", "Wheel to zoom, drag to move, click to pick a color, double-click to fit");

    public static string RefSplitterTip => T("左右にドラッグで幅を変える", "Drag left or right to resize");
}
