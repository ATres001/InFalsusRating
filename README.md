```markdown
# In Falsus Rating

为 In Falsus 提供类似 Arcaea Potential 的玩家评级系统。

> 此项目由 AI（DeepSeek v4）共同构筑。
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

## 输出文件

每次评级更新会导出两份文件到 `BepInEx\config\InFalsusRating\`：

- `b30.txt` — B30 排行（Top 30）
- `scores.txt` — 全部谱面成绩（含未通关）

---

## 问题排查

日志位于 `BepInEx\LogOutput.log`。

---

## 配置

| 文件 | 说明 |
|---|---|
| `name.txt` | 玩家名字（显示在 overlay 上） |
| `const.json` | 谱面定数覆盖（默认从游戏内自动 dump） |
| `bg.png` | overlay 背景图（尺寸决定窗口大小） |
| `font2.ttf` | 自定义字体（推荐 Furore） |

---

## 快捷键

- **F8** — 手动切换 overlay 显示/隐藏

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
- 第 2~6 首（B2~B6）：权重 ×2
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

## 致谢

- 字体：[Furore](https://www.fontsquirrel.com/fonts/furore) by Erik Kirtley (SIL Open Font License 1.1)

---

## 作者留言

目前该模组处于早期开发版本。出现问题、有建议，请随时联系我。

最终解释权归 ATRES (qq2162004452) 所有。此 Mod 为玩家自制模组，与 Lowiro 无直接关系。我将会持续维护且开源该项目，直至游戏内发布自身的 rating 机制或者收到任何的版权警告。

All rights reserved by Atres.
```
