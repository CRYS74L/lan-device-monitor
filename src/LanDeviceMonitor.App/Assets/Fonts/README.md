# 内嵌字体来源

文字字体采用 Noto Sans SC 简体可变字体。`NotoSansSC-VF.ttf` 原样取自本机已安装文件，包含完整 100–900 字重轴与 30,890 个字符映射；元数据版本为 `2.04;241114210130;non-release`。

| 文件 | SHA-256 |
|---|---|
| NotoSansSC-VF.ttf | 763146584CF0710223441356B4395E279021B0806C196614377A7A0174AE074A |
| NotoSansSC-SemiBold.ttf | ACC3276790DF63B1134E4694CA21CE875238E4826004C0BBADDAA582D65866D5 |
| SegoeIcons.ttf | 9252396FD902FD1A95775EC81F52C5818EE24F8A1297127BFBCCA2787C1B03E4 |

桌面直接内嵌完整可变 TTF。WPF 通过它的命名字重选择 Light / Regular / Medium；600 字重使用从同一可变字体生成的完整 SemiBold 实例，避免被匹配到 700。网页使用 `app-text-variable.woff2`，保留完整字重轴、字形、字符映射、字宽和布局表，按 CSS 的 100–900 字重选择，不依赖访问设备安装的字体。

`AppTextSpacing.ttf` 由生成脚本创建，仅提供空格与 U+2009 窄空格的空白字形，窄空格宽度为 0.2 em。其垂直度量与文字字体一致，采用 GPL-3.0-only；网页对应 `app-text-spacing.woff2`，仅用于 U+2009。图标独立使用内嵌的 Segoe Fluent Icons。

`scripts/build-web-fonts.py` 使用 fonttools 4.66.1 和 brotli 1.2.0 生成桌面 600 实例与网页字体。`--desktop-only` 仅生成桌面实例；`--spacing-only` 仅生成间距资源；`--icons-only` 仅生成图标资源。

Noto Sans SC 的版权归 Adobe 等原作者所有，使用 SIL Open Font License 1.1。完整版权与许可随包保留于 [NotoSansSC-OFL.txt](NotoSansSC-OFL.txt)，上游为 [Noto CJK](https://github.com/notofonts/noto-cjk)。
