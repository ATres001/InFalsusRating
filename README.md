# In Falsus Rating

为 In Falsus 提供类似 Arcaea Potential 的玩家评级系统。

> 此项目由 AI（DeepSeek v4 pro）共同构筑。
> 意见和反馈可以进入QQ群 384322205 提出，也可以来聊天和潜水，新版本包也会在群内发布！

编译前请修改 `csproj` 里的 `GameDir`。

---

## 前置条件

- BepInEx 6.0.0-be.7xx（IL2CPP x64 版本）

---

## 安装

1. 确保游戏已安装 BepInEx 6 (IL2CPP x64)
2. 首次启动游戏，让 BepInEx 生成 interop 程序集
3. 将 `plugins/InFalsusRating.dll` 复制到：
   ```
   游戏目录\BepInEx\plugins\
   ```
4. （可选）将 `config/InFalsusRating/` 里的文件复制到：
   ```
   游戏目录\BepInEx\config\InFalsusRating\
   ```
5. 启动游戏即可，评级数字会从顶部滑入显示

---

## 显示时机

Overlay 只在以下界面显示：

- 选曲界面（`SongSelectScene`）
- 结算界面（`ResultsScene`）

其他界面（标题、Hub、卡牌、故事、时间线、游玩中）自动隐藏。

---

## 快捷键

- **F8** — 手动切换 overlay 显示/隐藏
- **F9** — 导出 B30 图片（`b30.png` / `b30_nojacket.png`）

---

## 输出文件

每次评级更新会导出两份文件到 `BepInEx\config\InFalsusRating\`：

- `b30.txt` — B30 排行（Top 30）
- `scores.txt` — 全部谱面成绩（含未通关）

按 F9 导出 B30 图片：

- `b30.png` — 曲绘版 B30（需要 `jacket/` 文件夹，缺失时跳过）
- `b30_nojacket.png` — 无曲绘版 B30（**始终导出**）：30 行简化卡片
  （排名 / 定数 / 曲名 / 曲师 / 单曲 rating / 单曲 ext），
  顶部玩家信息（名字 / 总评级 / EXACTIFICATION），
  左下方 EXACTIFICATION B10 排行（位于 `b30.png`）

---

## B30 图片资源

放在 `BepInEx\config\InFalsusRating\` 下：

```
jacket/          曲绘（按曲目 base_name 命名，如 alamode.png）；
                 background_big.png 为曲绘版整图底图 (请在release版本中下载DLC1)
b30_cards/
  background/    曲绘版卡片底图（按 rank 分档 1.png / 2.png / 3.png）
  difficulty/    难度底板（按 曲目ID_难度 / 曲目ID / 难度 依次查找）
  score/         评分底板（0~4.png 分数分档 / 曲目ID_难度 谱面专属）
  fcpm/          pm.png / fc.png 徽章（PM 优先）
  decor/         装饰层（按 rank 分档 1.png / 2.png / 3.png）
  exact/         无曲绘版行背景（按难度
                 Minimal / Evolved / Ultimate / Forbidden .png）
  b30bg.png      无曲绘版整图背景（放在 exact/ 或 b30_cards/ 下均可，
                 其尺寸决定图片大小，原样不拉伸）
catalog.json     曲目元数据（id / title / artist / base_name），
                 曲名、曲师和曲绘文件名的权威来源
```

---

## 问题排查

日志位于 `BepInEx\LogOutput.log`。

---

## 配置

| 文件 | 说明 |
|---|---|
| `name.txt` | 玩家名字（显示在 overlay 和图片上） |
| `const.json` | 谱面定数覆盖（默认从游戏内自动 dump） |
| `catalog.json` | 曲目元数据（title / artist / base_name） |
| `bg.png` | overlay 背景图（尺寸决定窗口大小） |
| `font2.ttf` | 自定义字体（推荐 Furore） |

---

## 评级公式

### 单曲 rating

```
基础分 = 谱面定数 + 分数加成

分数加成 = clamp((分数 - 95,000,000) / 5,000,000 × 2.0, -2.0, +2.0)
```

若该谱面历史最高 dive ≥ 0：

```
rating = 基础分 + dive × 0.01
```

若历史最高 dive < 0（未通关）：

```
n         = |dive|                        （1 ~ 15）
sumDive   = -(1 + 2 + ... + n)
penalty%  = floor(sumDive / 1.2)
factor    = max(1 + penalty% / 100, 0.1)   （最多减 90%）
rating    = 基础分 × factor
```

dive 惩罚示例：

| dive | sumDive | sumDive/1.2 | floor | 惩罚% |
|---|---|---|---|---|
| -5  | -15  | -12.5  | -13  | 13%  |
| -10 | -55  | -45.8  | -46  | 46%  |
| -15 | -120 | -100.0 | -100 | 90%（封顶） |

### 总 rating

B30 加权平均，**未通关成绩也计入**：

- 第 1 首（B1）：权重 ×4
- 第 2~6 首：权重 ×2
- 第 7~30 首：权重 ×1
- 总权重 = 4 + 2×5 + 1×24 = 38

```
PlayerRating = Σ(rating_i × weight_i) / 38
```

### EXACTIFICATION

对每个谱面，取该谱面历史最高 EXACT 数（仅统计通关成绩）：

```
单曲 EXACTIFICATION 率 = 100 × EXACT数量 × 谱面定数 / 谱面总物量
总 EXACTIFICATION = Top 10 单曲率之和
```

---

## 开源协议

MIT License

---

## 第三方资产说明

THIRD-PARTY-NOTICES

---

## 致谢

- 字体：[Furore](https://www.fontsquirrel.com/fonts/furore) by Erik Kirtley (SIL Open Font License 1.1)
- 数据库：[InFalsusUnofficialB30](https://github.com/mewcodex/InFalsusUnofficialB30) by mewcodex

---

## 作者留言

目前该模组处于早期开发版本。问题，建议请随时告知

最终解释权归 ATRES (qq2162004452) 所有。此 Mod 为玩家自制模组，与 Lowiro 无直接关系。我将会持续维护且开源该项目，直至游戏内发布自身的 rating 机制或者收到任何的版权警告。

All rights reserved by Atres.
