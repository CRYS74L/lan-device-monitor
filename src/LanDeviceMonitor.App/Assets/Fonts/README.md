# 内嵌字体来源

文字字体采用 PingFang SC（苹方-简）的 Light、Regular、Medium、Semibold，OTF 文件保持原始字形、字宽及名称，不依赖系统安装的字体。

| 文件 | SHA-256 |
|---|---|
| PingFangSC-Light.otf | BDD6E2BC0EF1ECA5A38995F01DD31F2E54F804F8DBB0B36318F1124E648ECCC7 |
| PingFangSC-Regular.otf | 3E433EE51427151880326DE7101C11B68A4BA8BF695515296FF24286F69BEDD4 |
| PingFangSC-Medium.otf | 8E6A0B3FBA3AA759BF84E51F6D52558AA869C7AA9B74E159891175235EDE9F63 |
| PingFangSC-Semibold.otf | 488A6B6E8684AC9EBE7EB915C9BBF39E275A5E8F5348F992C952324FC57CF792 |
| SegoeIcons.ttf | 9252396FD902FD1A95775EC81F52C5818EE24F8A1297127BFBCCA2787C1B03E4 |

最后一个文件仅用于符号图标，文字始终使用同一套 AppTextFont。桌面字体编译为 WPF Resource，网页字体编译为 EmbeddedResource，由同一程序提供，不依赖访问设备的系统字体或外部字体服务。

`AppTextSpacing.ttf` 由生成脚本创建，仅提供空格与 U+2009 窄空格的空白字形，窄空格宽度为 0.2 em。它补齐苹方缺少的间距字符，采用 GPL-3.0-only；网页对应 `app-text-spacing.woff2`，仅用于 U+2009。

网页 WOFF2 分别来自上述四个 OTF，保留完整字符映射、字形与字宽，并按 300 / 400 / 500 / 600 匹配实际字重。生成工具为 `scripts/build-web-fonts.py`，使用 fonttools 4.66.1 和 brotli 1.2.0；本地依赖置于被忽略的 `.tools/font-build-deps`。

PingFang SC 字体版权信息：Copyright © 2015 DynaComware. All rights reserved. 字体来源说明指向 [PingFang-OTF-Fonts](https://github.com/jimmyctk/PingFang-OTF-Fonts)。第三方字体保留各自权利，项目的 GPL-3.0 许可不替代字体授权。
