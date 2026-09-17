In Falsus Rating
================
为 In Falsus 提供类似 Arcaea Potential 的玩家评级系统

编译前请改 csproj 里的 GameDir

【前置条件】
- BepInEx 6.0.0-be.7xx（IL2CPP x64 版本）

【安装】
1. 确保游戏已安装 BepInEx 6 (IL2CPP x64)
2. 首次启动游戏，让 BepInEx 生成 interop 程序集
3. 将 plugins/InFalsusRating.dll 复制到：
   游戏目录\BepInEx\plugins\
4. （可选）将 config/InFalsusRating/ 里的文件复制到：
   游戏目录\BepInEx\config\InFalsusRating\
5. 启动游戏即可，评级数字会从顶部滑入显示

【输出】
- 每次评级更新会导出 B30 列表到：
  BepInEx\config\InFalsusRating\b30.txt

【问题排查】
- 日志位于 BepInEx\LogOutput.log


【配置】
- `name.txt` — 玩家名字（显示在 overlay 上）
- `const.json` — 谱面定数覆盖（默认用内置的难度默认值）
- `bg.png` — overlay 背景图（尺寸决定窗口大小）
- `font2.ttf` — 自定义字体（推荐 Furore）

【快捷键】

- F8 — 手动切换 overlay 显示/隐藏

【评级公式】

- 单曲 rating = 谱面定数 + 分数加成(0~2) + dive/100
- 总 rating = B30 加权平均（#1 权重 4，#2-6 权重 2，#7-30 权重 1）
- 未通关（dive < 0）的成绩不计入

【开源协议】
MIT License

【致谢】
- 字体：[Furore](https://www.fontsquirrel.com/fonts/furore) by Erik Kirtley (SIL Open Font License 1.1)

【作者留言】
目前该模组处于早期开发版本。出现问题，有建议，请随时联系我

最终解释权归ATRES(qq2162004452)所有。此Mod为玩家自制模组，与Lowiro无直接关系。我将会持续维护且开源该项目直至游戏内发布自身的rating机制或者收到任何的版权警告。
All rights reserved by Atres.
