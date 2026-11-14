using Avalonia.Controls;
using PhotoViewer.Common.Localization;
using PhotoViewer.Common.Photoing;
using PhotoViewer.UI.Windowing;
using System;
using System.Threading.Tasks;

namespace PhotoViewer.Common.Windows;

public partial class GallerySettingsView : SettingsPageView
{
    public GallerySettingsView()
    {
        InitializeComponent();
    }

    public GallerySettingsView(SettingsViewModel vm, SettingsNavId navId, LangId? pageLabel = null) : this()
    {
        Initialize(vm, navId, pageLabel);
    }

    protected override void Build()
    {
        BindToggle(PART_ShowGalleryInFullscreen, ConfigId.ShowGalleryInFullscreen,
            LangId.Settings_ShowGalleryInFullscreen, LangId.Settings_Appearance);
        BindToggle(PART_ShowGalleryFileName, ConfigId.ShowGalleryFileName,
            LangId.Settings_ShowGalleryFileName, LangId.Settings_Appearance, true);
        BindToggle(PART_ShellThumbnail, ConfigId.EnableGalleryShellThumbnail,
            LangId.Settings_EnableGalleryShellThumbnail, LangId.Settings_Appearance, true);

        BindUIntSlider(PART_GalleryColumns, ConfigId.GalleryColumns,
            LangId.Settings_GalleryColumns, LangId.Settings_Appearance, 3u, PART_GalleryColumnsLabel);
        BindUIntSlider(PART_ThumbnailSize, ConfigId.ThumbnailSize,
            LangId.Settings_ThumbnailSize, LangId.Settings_Appearance, 70u, PART_ThumbnailSizeLabel);

        BindUIntInput(PART_GalleryCacheSize, ConfigId.GalleryCacheSizeInMb,
            LangId.Settings_GalleryCacheSizeInMb, LangId.Settings_Appearance, 100u);

        SetLocalizedText(PART_ClearCache, LangId._ClearCache);
        PART_ClearCache.Click += async (_, _) => await ClearThumbnailCacheAsync();
        RegisterSearchKey(PART_ClearCache, LangId._ClearCache, null, LangId.Settings_Appearance);
    }

    private async Task ClearThumbnailCacheAsync()
    {
        var owner = TopLevel.GetTopLevel(this) as PhWindow;
        Exception? error;

        PART_ClearCache.IsEnabled = false;
        try
        {
            error = await Task.Run(ThumbnailDiskCache.Clear);
        }
        finally
        {
            PART_ClearCache.IsEnabled = true;
        }

        if (error is null)
        {
            await ModalWindow.ShowInfoAsync(owner, new ModalWindowOptions
            {
                Title = Core.Lang[LangId._ClearCache],
                Heading = Core.Lang[LangId._ClearCache_Success],
            });
            return;
        }

        await ModalWindow.ShowErrorAsync(owner, new ModalWindowOptions
        {
            Title = Core.Lang[LangId._ClearCache],
            Heading = Core.Lang[LangId._ClearCache_Error],
            Details = error.Message,
        });
    }
}
