using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using PhotoViewer.Common.Localization;
using PhotoViewer.Common.Types;
using PhotoViewer.UI;
using PhotoViewer.UI.Windowing;
using ImageMagick;
using System.Runtime.InteropServices;

namespace PhotoViewer.Common.Windows;

public partial class AboutWindow : DialogWindow
{

    private const string _creditContent = """
        ◍ Avalonia                                         MIT licence
          https://github.com/AvaloniaUI/Avalonia
          Copyright (c) AvaloniaUI OÜ All Rights Reserved

        ◍ Magick.NET                                Apache-2.0 license
          https://github.com/dlemstra/Magick.NET
          Copyright (c) 2013-2026 Dirk Lemstra

        ◍ SkiaSharp                                        MIT licence
          https://github.com/mono/SkiaSharp
          Copyright (c) 2015-2016 Xamarin, Inc
          Copyright (c) 2017-2018 Microsoft Corporation

        ◍ Svg.Skia                                         MIT licence
          https://github.com/wieslawsoltes/Svg.Skia
          Copyright (c) 2020 Wiesław Šoltés

        ◍ NativeMemoryArray                                MIT licence
          https://github.com/Cysharp/NativeMemoryArray
          Copyright (c) 2021 Cysharp, Inc.

        ◍ NetCoreAudio                                     MIT licence
          https://github.com/mobiletechtracker/NetCoreAudio
          Copyright (c) 2020-2024 Fiodar Sazanavets
                                  (Scientific Programmer Ltd)

        ◍ CsWin32                                          MIT licence
          https://github.com/microsoft/CsWin32
          Copyright (c) Microsoft Corporation

        ◍ Fluent Emoji                                     MIT licence
          https://github.com/microsoft/fluentui-emoji
          Copyright (c) Microsoft Corporation

        ◍ D2Phap.EggShell                           Apache-2.0 license
          Copyright (c) 2024-2027 Dương Diệu Pháp

        ◍ D2Phap.FileWatcherEx                             MIT licence
          https://github.com/d2phap/FileWatcherEx
          Copyright (c) 2018-2027 Dương Diệu Pháp

        """;

    public AboutWindow()
    {
        InitializeComponent();

        IsButton1Visible = true;
        IsButton2Visible = false;
        IsButton3Visible = false;

        DefaultButton = DialogButton.Button1;
        DefaultFocus = DialogFocus.Button1;
        ShowInTaskbar = true;

        PART_LblCopyright.Text = "ImageGlass © 2010–2027 Duong Dieu Phap. GPLv3. No warranty.";
        PART_LblCreditContent.Text = _creditContent;

    }

    protected override void OnIgLanguageChanged()
    {
        base.OnIgLanguageChanged();

        Title = Core.Lang[LangId.Menu_MnuAbout];
        Button1Text = Core.Lang[LangId._Close];

        PART_LblSlogan.Text = Core.Lang[LangId._Slogan];
        PART_LblCredits.Text = Core.Lang[LangId._Credits];
        UpdateVersionText();
    }

    private void UpdateVersionText()
    {
        var magickVersion = MagickNET.Version;
        var dotnetVersion = RuntimeInformation.FrameworkDescription;

        PART_LblVersion.Text = $"""
            {Core.Lang[LangId._AboutVersion]} {Core.BuildInfo.Version}
            {magickVersion}
            {dotnetVersion}
            """;
    }

}
