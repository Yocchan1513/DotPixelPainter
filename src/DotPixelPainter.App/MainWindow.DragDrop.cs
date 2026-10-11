using DotPixelPainter.Core;
using Microsoft.UI.Xaml;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace DotPixelPainter;

/// <summary>エクスプローラーなどからファイルを画面に落として開く。</summary>
public sealed partial class MainWindow
{
    private static readonly string[] DroppableExtensions = [DotPixFile.Extension, ".png", PsdReader.Extension];

    private static bool IsDroppable(string path) =>
        DroppableExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    private void Root_DragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = T("開く", "Open");
        }
    }

    private void Root_Drop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }

        DataPackageView data = e.DataView;
        ErrorLog.Run("落としたファイルを開く", async () =>
        {
            var paths = new List<string>();
            var skipped = new List<string>();
            foreach (IStorageItem item in await data.GetStorageItemsAsync())
            {
                if (item is StorageFile && IsDroppable(item.Path))
                {
                    paths.Add(item.Path);
                }
                else
                {
                    skipped.Add(item.Name);
                }
            }

            if (paths.Count > 0)
            {
                await OpenFilesReplacingBlankAsync(paths, reportErrors: true);
            }

            if (skipped.Count > 0)
            {
                await ShowMessageAsync(
                    T("開けないファイルがありました", "Some files could not be opened"),
                    T("開けるのは .dotpix・.png・.psd のファイルです。", "Only .dotpix, .png and .psd files can be opened.") + "\n\n" + string.Join("\n", skipped));
            }
        });
    }
}
