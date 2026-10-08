# 莉莉丝 AI MOD 0.2.1

这是《The NOexistenceN of Lilith》桌宠的非官方社区 MOD，加入 AI 对话、文字与语音输入、中日文语音、无配音台词补音、天气查询和受限制的本机电脑操作。

> **本仓库是 [mimimi6666/Lilith-AI-Mod](https://github.com/mimimi6666/Lilith-AI-Mod) 的个人维护分支。可安装版本是 0.2.1。**
> 上游 `main`（`3f1e9ea`）对着当前游戏无法独立编译；本分支可以。
> 本分支沿用上游条款，不另授许可。补充台词语音包和动态语音运行时仍从原作者的 Release 下载，本仓库不重新发布这些语音内容。
> 差异清单见下方「相对上游的改动」。

## 相对上游的改动

1. **游戏内 AI 设置分页**（PR [#10](https://github.com/mimimi6666/Lilith-AI-Mod/pull/10)）：在官方设置左侧新增「AI」分页，分「当前模型」「提供商与聊天」「语音识别」三个子页；三页共用一份草稿，点「保存全部」统一生效；取消、Esc、切换官方分页或关闭设置都会丢弃未保存编辑；录音或请求处理中不可保存。
2. **多提供商配置集中到 `BepInEx/config/lilith-ai.json`**：聊天协议支持 `chat-completions` / `responses` / `anthropic` / `gemini`；语音识别协议支持 `gemini-audio` / `qwen-http` / `dashscope-realtime` / `audio-transcriptions`。提供商与模型使用稳定 ID，当前模型是 `{providerId, modelId}` 引用，不再拼接字符串。
3. **聊天与语音识别互相独立**：两边各自保存地址与凭据，可以使用同名 ID；实时识别失败只回退到「当前模型」页选定的 HTTP 回退。
4. **适配最新游戏 Build 的设置界面**：上游 `main` 仍引用新版游戏程序集已移除的 `UI.TraySetting.TraySettingView`，无法独立编译；本分支完成适配。
5. **AI 对话历史查看面板**（PR [#12](https://github.com/mimimi6666/Lilith-AI-Mod/pull/12)）：F7 输入框上方入口，显示最近 32 条记录，区分玩家与莉莉丝，支持滚轮浏览与拖动，带异常隔离。
6. **文字与语音同步**（PR [#11](https://github.com/mimimi6666/Lilith-AI-Mod/pull/11)）：文字最多延迟 7 秒，语音一旦就绪立即显示。
7. **安装器与语音运行时可靠性**：改进安装恢复流程，安装前校验语音运行时就绪状态。
8. **新增 4 个测试项目**：`LilithAiSettingsTests`、`LilithAiSettingsUiTests`、`LilithNativeVoiceCompatibilityTests`、`LilithVoicePolicyTests`（上游仓库没有 `tests/` 目录）。
9. **补充语音包改为可选项**：官方中／日文配音已覆盖绝大多数台词，安装器的「完整中／日文补充台词语音」复选框默认不勾选；勾选安装时，安装器会把 `BepInEx/config/community.lilith.textinjector.cfg` 的 `NativeVoicePackEnabled` 自动写为 `true`，插件端默认值改为 `false`，未安装语音包的玩家不会再走补充语音分支。

### 同步上游

```bash
git fetch upstream
git merge upstream/main
```

### 许可

本分支沿用上游条款，未另外授予任何许可，详见文末「非官方 AI 语音 MOD 声明」。

## 上游 0.1.1-RC4 中已有的更新

- 兼容官方 Build `24273498` 的分类设置界面。
- MOD 设置只在“控制”分页显示，不再覆盖其他设置。
- 修复 F6／F7 第二次无法触发，以及按键重新绑定与 Esc 取消失效。
- 中文／日文内置与补充语音切换继续保持相互独立。
- 加入已测试的千问聊天与语音识别、服务商原生联网搜索，以及明确电脑指令的本机可靠路由。
- 应用启动可从运行中程序、Windows 开始菜单、Store／MSIX 注册与快捷方式解析，不写死玩家路径。
- 修复 API Key 窗口残留鼠标／键盘焦点，并将原生权益兑换与 API Key 输入完全分离。

## AI 服务兼容性

**目前仍推荐优先使用 Gemini。** 角色人格、上下文、多语言输出、日文显示／语音分离、Google Search、AI 信件和自然语言电脑工具，均主要以 Gemini API 开发。

千问已经完成实际接入测试，默认使用 `qwen3.7-plus` 聊天、`qwen3-asr-flash` 识别语音，并支持服务商原生联网搜索以及明确应用、媒体、截图和受审核电脑指令的本机路由。可用性、免费额度和计费要求取决于玩家的阿里云模型服务账户与地区。

OpenAI 与 DeepSeek 目前仅提供**实验性的文字聊天兼容层**。基本请求已经实现，成功返回的文字也可能由本地 GPT-SoVITS 朗读，但尚未针对各模型、响应格式、额度、地区限制及后续 API 变更进行完整测试。这两项服务目前没有接入服务商原生的联网搜索与工具调用；只有部分明确命令仍可由 MOD 在本机直接识别。

`F6` 使用「当前模型」页选定的语音识别模型。聊天选了 OpenAI 或 DeepSeek 时，不再强制改走 Gemini 识别。

## 一键安装

1. 运行 `LilithAI-Mod-Setup.exe`。
2. 安装程序会读取 Steam 与所有 Steam Library，自动寻找游戏。只有无法定位时才需要选择包含 `Lilith.exe` 的文件夹。
3. 保持需要的组件勾选，点击“安装／更新”。
4. 首次安装 AI 动态语音时，安装程序会建立独立的推理环境，不会修改玩家电脑已有的 Python。
5. 启动后打开游戏设置的“AI”分页，配置提供商连接与模型，并选择聊天和语音识别模型。F6／F7 仍在“控制”分页。

官方 RC4 安装包有可选的「Codex 工作状态桥接」。本分支安装器按公开源码编译，没有这个勾选。已经用官方安装包勾过的 Hooks 会保留，本分支的更新和卸载都不会改动它们。游戏仍会读取已经写好的事件。

安装包不包含作者 API Key、聊天记录、玩家姓名、日志、截图、训练数据集或开发电脑路径。更新也不会覆盖已有 API Key、聊天记忆、按键和其他玩家设置。

## 游戏内 AI 配置

打开官方设置左侧的“AI”分页。界面沿用官方分页、字体和控件，分为“当前模型”“提供商与聊天”“语音识别”三个管理子页：

- **当前模型**：分别选择聊天模型、ASR 模型与可选 ASR 回退，并查看本地工具支持状态。
- **提供商与聊天**：按自定义模型 API 的顺序配置。连接页填写 Provider ID、显示名称、API 地址、API 协议和凭据，下一步进入模型；可获取可用模型后搜索、勾选并添加所选，也可手动添加。模型页填写标识、显示名称、上下文窗口和输出上限。推理、温度、兼容字段和原生搜索在高级参数里。连接名称只用于显示，不决定请求协议。
- **语音识别**：按与聊天相同的三步配置。连接页填写 Provider ID、显示名称、API 地址和凭据，下一步进入语音模型；模型页选择语音协议并填写标识，HTTP 可填完整接口，实时识别要填 WebSocket 地址。当前使用的模型和 HTTP 回退在“当前模型”里选择。语音连接单独保存地址和凭据，语音协议按模型选择。

所有子页共用一份草稿，点击“保存全部”统一保存，下一次聊天或录音生效；每次操作使用开始时取得的配置与凭据快照。取消、Esc、切换官方分页或关闭设置都会丢弃未保存编辑。录音、转录或 AI 请求处理中不可保存。删除仍被当前聊天、ASR 或回退引用的提供商／模型后，必须重新选择才能保存。

### 协议与地址

聊天协议按提供商选择，一个提供商只有一种。这与 DeepSeek Harness 的自定义模型 API 相同：OpenAI Chat Completions、OpenAI Responses、Anthropic Messages。网关同时提供两种协议时，建成两个提供商。本 MOD 另外保留 Gemini。

| 界面 | 配置协议 | 也接受的写法 | 请求路径（追加到 Base URL） |
| --- | --- | --- | --- |
| Chat | `chat-completions` | `openai-completions` | `/chat/completions` |
| Responses | `responses` | `openai-responses` | `/responses` |
| Anthropic | `anthropic` | `anthropic-messages` | `/messages` |
| Gemini | `gemini` |  | `/models/{model}:generateContent` |

别名只在读取时换成第一列的名字。提供商上的协议会写回它的每个聊天模型。

新增提供商要填小写 Provider ID，创建后不能改。显示名称、Base URL、协议、凭据和模型仍可改。要换 ID，新建一个提供商，把当前模型改过去，再删除旧的。已有的 `Gemini`、`Qwen` 这类迁移 ID 可以继续使用。

Base URL 必须包含服务需要的版本路径，例如 `https://your-provider.example/v1`。聊天请求不猜测或补加 `/v1`。模型目录是例外：OpenAI 两种协议请求 `GET {Base URL}/models`；Anthropic 在目录地址缺少 `/v1` 时补上这一段，再请求 `GET {根}/v1/models?limit=1000`。聊天仍使用原来的 Base URL。获取结果只进草稿，保存后才写入磁盘。

模型标识填写服务实际接受的名称，也可以在目录里搜索、勾选后添加所选。上下文窗口只记在模型上，不随请求发送，也不裁剪 32 轮记忆。输出上限、温度和推理参数默认留空，由服务端决定。`reasoningEffort` 写成 `off` 时不发送推理参数。Chat Completions 默认发送 `max_completion_tokens`；网关若拒绝这个字段，把 `maxTokensField` 设为 `max_tokens`。Anthropic Messages 要求显式填写输出上限。

ASR 支持 `gemini-audio`、`qwen-http`、`dashscope-realtime` 和 `audio-transcriptions`。协议写在每条语音模型上，不跟聊天协议。HTTP ASR 可以覆盖完整接口地址；实时 ASR 必须明确填写 WebSocket 地址。没有单独的语音模型目录，模型标识手动填写。聊天和识别各自保存连接与凭据，两边可以使用同一个 ID。实时识别失败只使用当前模型页选好的 HTTP 回退。

本地工具策略可选 `auto`、`on`、`off`：`auto` 在现有权限允许时随实际请求发送工具声明，不发送额外探测请求；只有服务明确拒绝工具参数且尚未执行工具时，才重试一次纯聊天。`on` 被拒绝会报错，`off` 不发送工具。普通文字回复不会被判定为不支持工具，识别状态只保留在当前运行期。该策略不会自动开启“高级电脑操作”。原生联网搜索是独立选项，不等同于本地工具支持；旧 Gemini／千问搜索模式会保留，新连接默认关闭原生搜索。

### 配置文件与凭据

唯一 AI 配置源是游戏目录内的 `BepInEx/config/lilith-ai.json`，可以通过界面或手工编辑。提供商与模型使用稳定 ID，当前模型引用是 `{ "providerId": "...", "modelId": "..." }`，不是拼接字符串。例如，无需认证的本地兼容服务：

```json
{
  "version": 1,
  "providers": {
    "local": {
      "name": "本地服务",
      "baseUrl": "http://localhost:1234/v1",
      "protocol": "chat-completions",
      "credential": { "mode": "none" },
      "chatModels": {
        "chat": { "model": "your-chat-model", "name": "聊天模型", "protocol": "chat-completions", "tools": "auto" }
      }
    }
  },
  "asrProviders": {
    "local": {
      "name": "本地语音",
      "baseUrl": "http://localhost:1234/v1",
      "credential": { "mode": "none" },
      "models": {
        "audio": { "model": "your-asr-model", "name": "语音识别", "protocol": "audio-transcriptions" }
      }
    }
  },
  "chat": { "providerId": "local", "modelId": "chat" },
  "asr": { "providerId": "local", "modelId": "audio" }
}
```

旧的 `lilith-ai.json` 如果还没有 `asrProviders`，下次读取会把提供商里的语音模型拆到 `asrProviders`。本地 Key 会复制到 `asr:` 加上 Provider ID，聊天原来的 Key 不动。没有聊天模型的连接会从聊天列表移到语音识别。这次拆分先留在内存里，要点“保存全部”才写入磁盘。文件里已经有 `asrProviders` 时，不再从旧的 `asrModels` 导入。

凭据支持三种模式：

- `stored`：`reference` 指向独立 `lilith-ai.credentials.json` 中的键名。聊天默认用 Provider ID，语音识别默认用 `asr:` 加上 Provider ID，两边不共用同一条 Key。界面填写的 Key 只存入此文件，不写入主配置；本地文件不是加密保险库，不要公开它。
- `env`：`reference` 是环境变量名称，例如 `LILITH_API_KEY`。读取游戏进程的环境变量，不执行获取凭据的 Shell 命令。
- `none`：无需认证，不发送认证凭据。

已保存的 Key 显示为“已配置”，编辑时遮罩；留空不修改，删除须点击明确清除按钮。环境变量缺失、模型引用无效或配置格式错误会报错，不自动换供应商。保存失败时保留原文件和界面草稿。

首次启动且新配置不存在时，一次性备份并迁移旧 `.cfg` 中的供应商、模型、当前选择及凭据：原千问用户保留实时 ASR 与 HTTP 回退，其余用户保留 Gemini ASR。Gemini 与千问会另外写成独立的语音连接，本地 Key 复制到 `asr:Gemini` 和 `asr:Qwen`。旧文件保留供回滚，此后不再覆盖新配置；新配置损坏也不会重新迁移。回滚旧 DLL 时使用对应旧配置备份，不要把凭据文件加入仓库。

该界面不修改游戏配音语言。日文剧情／动作继续使用官方配音，AI 新生成回复仍进入现有中文／日文语音合成管线，中文缺失台词补充不变。

## 操作

- 默认 `F7`：打开文字输入气泡。
- 默认按住 `F6`：录音；松开后识别并发送。
- 两个按键都可在游戏设置中重新绑定。
- 中文／日文语音选择会保留到下次启动。
- F7 输入提示、AI 输入输出与语音识别会跟随当前界面语言。
- “高级电脑操作”默认关闭；开启后 AI 才能使用白名单中的应用程序、媒体键、窗口、截图、计时器、锁定与睡眠等功能。

## 硬件与网络

- 核心 MOD：Windows 10/11 x64。
- AI 对话与语音识别：需网络和玩家自己的 API Key。
- AI 动态语音：NVIDIA 显卡体验更好，建议 8 GB 以上显存。无兼容 NVIDIA 显卡时会安装 CPU 版，建议至少 16 GB 内存，生成速度会较慢。
- 完整补充台词包约 301 MB（可选，安装程序默认不勾选）；动态语音模型包约 1.98 GB，安装时还需额外空间建立推理环境。
- 官方已更新中文与日文语音并覆盖绝大多数台词，补充台词语音包仅在需要为极少数仍无官方配音的台词补音时安装。

## 隐私与排错

- GPT-SoVITS 在本机 `127.0.0.1` 运行，不对外开放连接。
- 对话、语音识别会发送给玩家所选 AI 服务商，受该服务商条款与隐私政策约束。
- 天气定位仅使用公网 IP 的粗略城市位置，MOD 不保存 IP 地址。
- MOD 日志：`BepInEx\LogOutput.log`
- 语音主机日志：`BepInEx\data\LilithTextInjector\voice-runtime\logs\voice-host.log`
- 语音安装日志：`BepInEx\data\LilithTextInjector\voice-runtime\voice-runtime-install.log`

## 非官方 AI 语音 MOD 声明

本 MOD 为玩家自行制作的非官方、非商业性游戏模组，仅供游戏交流与个人娱乐使用。

本 MOD 与游戏开发商、发行商、角色著作权人、录音制作者及原配音演员不存在合作、授权、赞助或背书关系，亦不代表任何官方立场。

本 MOD 中的新增语音内容由人工智能技术生成或合成，并非原配音演员本人重新录制。请勿将相关内容误认、剪辑或宣传为原配音演员本人发言、官方录音或官方新增内容。

游戏名称、角色名称、角色形象、原作台词、音频及其他相关素材之权利，均归其各自合法权利人所有。本 MOD 作者不主张拥有上述原作内容的著作权、商标权或其他专有权利。

本 MOD 免费提供，不得以任何形式销售、付费转载、付费分发，或用于商业广告、语音代言、政治宣传、欺诈、侮辱诽谤、成人内容及其他可能损害原配音演员或相关权利人合法权益的用途。

未经允许，不得提取、转售、重新训练或重新发布本 MOD 所包含的 AI 语音内容，亦不得将其用于制作冒充原配音演员本人，或可能造成公众混淆、误认的内容。

使用者应自行遵守所在地的法律法规、游戏用户协议、MOD 政策及发布平台规则。若相关权利人认为本 MOD 涉及权益问题，可通过以下方式联系作者：

对本分支安装包的权利通知请联系发布者。语音模型仍来自上游发布，未经允许不要再分发。上游作者米米：mimimi5206666@gmail.com。

联系方式：**shxiao1115@163.com**

发布者收到具体且可验证的权利通知后，将及时核实，并视情况停止提供、删除或调整相关内容。

发布者：Shxiao
上游项目：[mimimi6666/Lilith-AI-Mod](https://github.com/mimimi6666/Lilith-AI-Mod) 0.1.1-RC4
版本：0.2.1
发布日期：2026/10/08
