# 校验与错误行为

这一页是模块输入校验、错误呈现与动作闸门的**唯一权威口径**。控件只做字符准入，模块负责语义；框架只把结论画出来，不替模块判断业务。

本文只描述提示、状态与错误**表示什么**，不规定具体措辞。具体文案由各模块文本表与开发者调整；任何“应该显示某句话”的示例都会过期。

## 1. 五类问题，五种落点

| 类型 | 典型例子 | 字段 | 状态行 | 动作 |
|---|---|---|---|---|
| Format | 解析失败、数值越界、非法 IP/主机名、HEX 不完整 | 红框 + 悬停原因 | **不写** | 拒绝应用/禁用动作 |
| Module | 必填为空、组号/通道号重复、Nyquist、上下界互逆、总帧率、无启用通道 | 红框 + 悬停原因 | 阻止动作时**可复述一次**具体原因 | 禁用或拒绝 |
| 前置条件缺失 | 未选串口、动态选择器没有可用项 | **不标红** | 写“缺什么” | 禁用动作 |
| 运行时失败 | 连接拒绝、端口占用、设备丢失、写盘失败 | **不标红** | 写结果；详细原因进 Issue | 保持可重试 |
| 警告 | 发送间隔为 0 | 不红，给 Warning（悬停） | 可写一句提示 | 只禁用对应用户动作 |

**同一原因只报一次（含例外）**：

- 纯 Format 错误绝不写状态行；原因只在字段悬停里。
- Module 错误在**阻止了某个动作**时，允许在状态行复述一次——否则按钮灰着却看不到原因。字段悬停仍保留更具体的那一句。
- 警告允许进状态行，因为字段 warning 本身不是常显的。
- 除以上三种，同一原因不得在字段、状态与 Issue 三处各写一遍。

**来源优先级**：同一字段同时命中 Format 与 Module 时 Module 优先。模块应**先收集、后一次性应用**结论（不要在不同分支里交替 `Set` 真/假），否则清除顺序会把刚设上的红冲掉。

## 2. 字段与动作职责

### 字段

- 使用 `InputValidity.Set` 写红框与原因；不要直接 `SetOutlineRole` 写普通输入的红框。
- 非法输入**保留上一份有效值**，文本不滚回；修正后清除红框。
- 范围/格式用具体键（如 `invalid.range`、`invalid.address`），不要统一写“无效数字”。
- 可归一化字段不得静默夹紧；越界按 Format 错误处理。只有文档明确允许的显示范围钳制才生效。
- 空必填文本按 Module 错误（红 + 状态）；**动态选择器**没有可用项时只给状态，不标红。
- 地址类字段不能只靠控件判断：`RemoteHost`/`RemoteAddress` 允许主机名，必须由模块同时校验“IP 或主机名”；纯 IP 字段才使用 `ValueTextFormats.IpAddress` 的整段判定。
- 纯数字/点的候选不能交给 `Uri.CheckHostName` 兜底：它会把 `999.999.999.999` 当成 DNS 名。模块必须先按 IPv4 候选规则判定，再回落到主机名。

### 状态行

- 纯显示/转换模块可以没有状态行，只保留字段红框与悬停原因。
- 发送动作被拒绝、连接/监听/绑定/写盘失败、数据处理停止时写状态。
- 状态只写一句；不把异常原文直接给用户（详细原因进 Issue/日志）。
- 状态只在**结果变化**时写，不逐帧重复。
- 未就绪时状态行说明参数未就绪，具体字段原因留在悬停里；不要把同一条字段文案既写字段又写状态。

### 按钮

- 用 `context.Appearance.SetEnabled`，模块缓存上次状态，只在变化时写。
- 发送/连接按钮在**确定性非法/前置条件不满足**时禁用；运行时失败后重新可用，以支持重试。
- 停止/关闭只在对应动作正在进行时可用；**空闲时配置非法不等于“可以停止”**。
- 被禁用按钮的原因必须能从字段红框、状态句或悬停提示得到，不能“灰着但不说”。

## 3. 事件链与幂等

### 配置字段

```text
用户键入
  → ModuleTextSurface.OnEditingUserEdited
    → PreviewValue(name, text)
      → SubmitInput(name, text)          // 不写配置、不写 ViewState
  → 模块 OnStep(Input)：先排空输入，再排空命令
    → OnInput(source, text)              // 模块把 text 存成本字段草稿
      → ValidateConfiguration()          // 草稿优先，没有草稿才读已提交配置
  → 回车/失焦（OnExplicit）
    → CommitValue → 框架写回配置 → OnConfigurationChanged
      → 模块重新校验；与配置相等的草稿自然清掉
```

- 配置字段（会重开会话的字段）用 `CommitMode="OnExplicit"`；普通模块值与声明值默认 `OnChange`。
- 键入过程只产生红框/警告与按钮状态，**不反复写配置**；否则每个键都会触发一次配置变更、重启通知与工作区写入。
- `Values.ReadText` 对**配置字段**读已提交配置；对**声明值**读视图状态。用户正在编辑的配置文本只经 `OnInput` 的 `text` 进入模块。
- 普通刷新只同步显示，**不再投递预览**。控件在首次取到某个值时投递一次预览，让模块校验界面此刻显示的内容；重开模块窗口也借此把未提交草稿恢复成已保存值。
- 命令必须看到最新输入：`OnStep` 先排空输入、再排空命令；打开/启动命令在动作边界再校验一次，未就绪时**拒绝请求**，不能只靠按钮置灰。

### 声明值

- 逐键写入视图状态后，模块在输入事件里读取；每拍轮询只允许作为“视图状态没有逐键投递”的兜底，且必须比较变化后再处理。
- 数据帧、会话探活、定时器拍**不得**重新解析配置字段做语义校验。

## 4. 各模块落点

### 4.1 外部接入（串口 / TCP / UDP）

| 模块 | 字段 | 规则 | 空值 | 阻止 |
|---|---|---|---|---|
| TcpClient | RemoteHost | 非空；拒绝 `0.0.0.0` / `::` / `*`；IP 或主机名 | Module | Open |
| TcpClient | RemotePort | 1–65535 | Module | Open |
| TcpClient | ConnectTimeout | 1–60 | Module | Open |
| TcpServer | LocalAddress | 下拉；合法 IP；允许 `0.0.0.0` / `::` | 仅状态，不红 | Start |
| TcpServer | LocalPort | 0–65535（0 = 系统分配） | Module | Start |
| TcpServer | MaxConnections | 1–256 | Module | Start |
| Udp | LocalAddress | 下拉；合法 IP；允许 `0.0.0.0` / `::` | 仅状态，不红 | Bind |
| Udp | LocalPort | 0–65535 | Module | Bind |
| Udp | RemoteAddress | 可选；空 = 只收；非空 = IP 或主机名，拒绝通配 | 合法 | 不阻止 Bind |
| Udp | RemotePort | 0–65535；目标地址非空时 1–65535 | 目标为空时合法；目标非空时 Module | 不阻止 Bind |
| Serial | PortName | 下拉；非空即可 | 仅状态，不红 | Open |

- 运行时失败（拒绝/超时/端口占用/设备丢失）只写状态，不调用 `InputValidity`；字段保持有效、动作保持可重试。
- 打开后字段禁用；未就绪时动作边界拒绝打开；配置被框架写回后重新校验。

### 4.2 波形发生器

| 字段 | 规则 | 来源 |
|---|---|---|
| 组名 / 通道名 | 非空、长度上限 | Format |
| 组编号 | 正整数且未被其它组占用 | 格式 / 重复 = Module |
| 通道编号 | 1–64 且组内不重复 | 格式 / 重复 = Module |
| 频率 / 周期 / 每周期点数 | 正数；2–4096 | Format |
| 相位 / 偏移 / 幅值 / 载波相位 | 有限实数 | Format |
| 占空比 | 0.01–0.99 | Format |
| 载波频率 | 正数；Nyquist 由组帧率判定 | Format / Nyquist = Module |
| 调制比 | 按波形种类取范围 | Format |
| 发送周期数 | 1–1 000 000 | Format |
| 组 ID 重复、总帧率、无启用通道 | 发送前校验 | Module |

- 状态优先级：发送中保持运行状态 → 第一条 Module 字段错误 → 发送前校验原因 → 仅格式错误时说明有字段未通过校验 → 无错误时可发送。
- 有任一字段错误时状态**不得表示为可发送**；开始类按钮禁用，停止按钮只在发送中可用。
- 切换组/通道时，若旧设置项的错误值被丢弃而新对象无错误，状态行说明旧错误值未被采用、已恢复已保存值；新对象自身的错误优先显示。
- 发送中改出非法配置不自动停发，但下一次开始被拒绝；结构变化（加/删组/通道）仍停止发送并提示。

### 4.3 发送器

| 模块 | 字段/动作 | 规则 |
|---|---|---|
| TextSender / HexSender | 间隔为空/非法 | 红框 + 禁用连续开关 |
| TextSender / HexSender | 间隔为 0 | 不红，warning/状态说明 0 只能单次发送、连续发送需大于 0；连续开关禁用 |
| TextSender / HexSender | 旧状态/程序化连续请求 0 | 间隔按 Module 错误标红、连续不成立、状态说明 |
| HexSender | 当前页 HEX 语法非法 | 发送按钮禁用、字段红框 |
| TextSender / HexSender | 当前页为空 | 发送按钮保持可点，点击后说明当前页为空、没有发送 |

### 4.4 显示与转换

| 模块 | 字段错误 | 状态 | 动作 |
|---|---|---|---|
| AttitudeDisplay | 组/三轴通道、平滑、视角、失联延时；三轴去重 | 仅坏帧/数据提示 | 无主动动作 |
| GaugeDisplay / ScaleBar* | 组/通道、上下界、零延时、平滑、小数位 | 不重复字段 | 无主动动作 |
| WaveformDisplay | X/Y 起止、刻度、历史/跟踪范围、保存参数 | 数据处理/保存结果 | 无数据时保存按钮禁用 |
| HexStringConverter | HEX 解析 | 不需要，红框即可 | 无主动动作 |
| RawToWaveform | 帧头/帧尾（仅文本帧）、组号、采样率 | 未处理/停止原因 | 非法时停止出帧 |
| WaveformToRaw | 帧头/帧尾（仅文本帧）、组号、小数位 | 未处理/停止原因 | 非法时停止编码 |
| DataMonitor | 无字段校验 | 保存/缓冲/运行时 | Save 保持可点 |

- 跨字段冲突（三轴重复、上下界互逆、X/Y 范围冲突）所有相关字段同时标红，原因可定位；红框在后续刷新中保持，修正后一起清除。
- 转换器的必填字段为空按 Module 处理；非法时停止出帧，修正后恢复。

## 5. 禁止事项

- 不在控件里写业务校验，也不在模块里重复 `TextFormat` 已挡住的字符检查。
- 不把运行时失败画成字段红框。
- 不同时用字段红框和状态行描述同一个纯格式错误。
- 不静默把 0、空值或越界值改成另一个值；不自动修复用户输入。
- 不在每拍刷新/数据帧里重新解析配置字段。
- 不新增没有 i18n 键的错误句子；不留死键。

## 6. 示例

字段错误：

```csharp
if (!TryParseInRange(text, 0, 6, out var decimals))
{
    _validity.Set(context, DecimalsSource, true, Tr(MyText.InvalidDecimals), ValueValidityOrigin.Format);
    return;
}

_validity.Set(context, DecimalsSource, false);
```

跨字段冲突（先收集，再一次性应用）：

```csharp
if (candidate.Maximum <= candidate.Minimum)
{
    var message = Tr(MyText.RangeConflict);
    _validity.Set(context, MinimumSource, true, message, ValueValidityOrigin.Module);
    _validity.Set(context, MaximumSource, true, message, ValueValidityOrigin.Module);
    return;
}
```

动作闸门（动作边界也要拒绝）：

```csharp
var canSend = !_sending && _fieldErrors.Count == 0 && TryValidateForSending(out var reason, out _, out _);

if (!canSend)
{
    PublishStatus(reason ?? Tr(MyText.BlockedByFields));
}

context.Appearance.SetEnabled(SendCommand, canSend);
context.Appearance.SetEnabled(StopCommand, _sending);

// 外部接入：命令处理里再检查一次，未就绪不请求打开
public bool HandleCommand(string commandName)
{
    if (commandName == OpenCommand)
    {
        if (!_openReady) return true;   // 拒绝，但不伪造运行时失败
        RequestOpen();
    }

    return true;
}
```

## 7. 验收清单

- 非法输入不能触发对应动作；修正后红框与禁用自动恢复。
- 跨字段冲突时所有相关字段同时标红，原因能定位到出问题的字段，且在后续刷新中保持。
- 运行时失败不标红字段、不禁用重试；状态句不含原始异常全文。
- 同一原因没有字段 + 状态 + Issue 三份描述；空页/空选择器符合上面的例外。
- 配置字段的 `[ConfigurationField(Minimum/Maximum)]` 在写回时强制执行，超出范围即拒绝，旧值保持不变。
- 配置字段键入过程不写配置；普通刷新/数据帧不产生新的输入事件或配置解析。
- 未就绪时从命令/快捷键/程序化路径也不能打开/启动。
- 地址等无法用范围表达的语义由对应模块读取字段后标红并禁用它所影响的操作；不在框架层猜测业务地址是否可达。
