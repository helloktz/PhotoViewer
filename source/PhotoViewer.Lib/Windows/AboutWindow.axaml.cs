/*
ImageGlass - A Fast, Seamless Photo Viewer
Copyright (C) 2010 - 2027 DUONG DIEU PHAP
Project homepage: https://imageglass.org

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with this program.  If not, see <https://www.gnu.org/licenses/>.
*/
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using PhotoViewer.Common.Localization;
using PhotoViewer.Common.Types;
using PhotoViewer.UI;
using PhotoViewer.UI.Windowing;
using ImageMagick;
using System;
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

        ◍ ImageGlass logo idea
          Nguyễn Quốc Tuấn
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

        PART_LblCopyright.Text = $"Copyright © 2010-{DateTime.UtcNow.Year} Dương Diệu Pháp";
        PART_LblCreditContent.Text = _creditContent;

    }


    #region Override Methods

    protected override void OnIgLanguageChanged()
    {
        base.OnIgLanguageChanged();

        Title = Core.Lang[LangId.Menu_MnuAbout];
        Button1Text = Core.Lang[LangId._Close];

        PART_LblSlogan.Text = Core.Lang[LangId._Slogan];
        PART_LblCredits.Text = Core.Lang[LangId._Credits];
        UpdateVersionText();
    }

    #endregion // Override Methods



    #region Private Methods

    private void UpdateVersionText()
    {
        var magickVersion = MagickNET.Version;
        var dotnetVersion = RuntimeInformation.FrameworkDescription;

        PART_LblVersion.Text = $"""
            {Core.Lang[LangId._AboutVersion]} {Core.BuildInfo.FullVersion}
            {magickVersion}
            {dotnetVersion}
            """;
    }


    #endregion // Private Methods

}
