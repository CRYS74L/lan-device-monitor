# Lucide 局部适配

`tablet.svg` 基于当前固定版本的 Lucide `tablet`，按用户要求稍加宽机身，并把底部实心点改成小圆环，以便与手机区分。保留 Lucide 线条语言与许可证归属。

`scripts/build-device-icons.py` 优先读取本目录中的同名覆盖文件；其余图标读取 `ThirdParty/lucide/icons`。桌面和网页统一从这一来源生成，不分别修改生成结果。
