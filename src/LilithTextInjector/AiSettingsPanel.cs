using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Il2CppInterop.Runtime;
using TMPro;
using UI.TraySettingNew;
using UI.TraySettingNew.SettingItems;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Localization.Components;
using UnityEngine.Localization.Events;
using UnityEngine.UI;

namespace LilithTextInjector;

// Native AI tab, paginated editor and draft lifecycle. All visuals are cloned from official settings prefabs.
internal static class AiSettingsPanel
{
    private static TraySettingNewView? _view;
    private static TraySettingTabItem? _tab;
    private static GameObject? _pageRoot;
    private static AiSettingsDraft? _draft;
    private static readonly List<GameObject> Rows = new();
    private static readonly List<Action> RestoreTabLayouts = new();
    private static readonly Dictionary<string, string> InvalidInputs = new();
    private static Button? _save;
    private static TMP_Text? _status;
    private static string _error = "";
    private static string _screen = "current";
    private static string _providerId = "";
    private static string _modelId = "";
    private static int _page;
    private static int _row;
    private static bool _nativeRootActive;
    private static Action<AiModelReference?>? _pick;
    private static bool _pickChat;
    private static bool _pickNone;
    private static bool _open;
    private static bool _formNew;
    private static string _formReturn = "providers";
    private static string _newProviderId = "";
    private static string _newProviderName = "";
    private static string _newBaseUrl = "";
    private static string _newProtocol = "chat-completions";
    private static string _newCredentialMode = "stored";
    private static string _newEnvName = "";
    private static string _newKey = "";
    private static string _modelReturn = "models";
    private static CancellationTokenSource? _catalogCts;
    private static Task<IReadOnlyList<string>>? _catalogTask;
    private static IReadOnlyList<string>? _catalogIds;
    private static string _catalogError = "";
    private static string _catalogQuery = "";
    private static readonly HashSet<string> CatalogPicked = new(StringComparer.Ordinal);
    private static bool _keyboardArmed;
    internal static bool IsOpen => _open;
    private static string T(string zt, string zs, string ja, string en) => DialogueManagerUpdatePatch.LocalizedText(zt, zs, ja, en);
    internal static string ErrorText(Exception e)
    {
        var summary = e is AiConfigurationException || e is AiServiceException ? e.Message
            : e is TimeoutException || e is System.Threading.Tasks.TaskCanceledException ? "AI operation timed out." : "AI connection or response failed.";
        return T("AI 設定／服務錯誤：", "AI 配置／服务错误：", "AI設定／サービスエラー：", "AI configuration/service error: ") + summary;
    }
    internal static void Update(TraySettingNewView view)
    {
        if (_view != view) { Reset(); _view = view; }
        if (_tab == null && view._tabItemPrefab != null && view._tabItemContainer != null)
        {
            _tab = UnityEngine.Object.Instantiate(view._tabItemPrefab, view._tabItemContainer);
            _tab.name = "LilithAiTab";
            _tab.OnClicked = null;
            _tab.enabled = false;
            _tab._button.onClick = new Button.ButtonClickedEvent();
            _tab._button.onClick.AddListener(DelegateSupport.ConvertDelegate<UnityAction>((Action)Open));
            _tab.gameObject.SetActive(true);
            RememberTabLayouts();
        }
        if (_tab != null)
        {
            FitTabs();
            _tab._titleText.text = "AI";
            _tab._image.sprite = _open ? _tab._selectedSprite : _tab._unselectedSprite;
        }
        if (!_open) return;
        // The game's selected/hover state can reapply its image after a tab click.
        foreach (var tab in view._tabItems) { tab._image.sprite = tab._unselectedSprite; tab._image.overrideSprite = tab._unselectedSprite; }
        if (!view.IsVisible || Input.GetKeyDown(KeyCode.Escape)) { Close(); return; }
        if (_save != null) _save.interactable = _draft != null && InvalidInputs.Count == 0 && DialogueManagerUpdatePatch.CanChangeAiSettings;
        if (_catalogTask == null || !_catalogTask.IsCompleted) return;
        try { _catalogIds = _catalogTask.Result; _catalogError = ""; }
        catch (Exception e)
        {
            var inner = e is AggregateException aggregate ? aggregate.InnerException ?? e : e;
            _catalogIds = Array.Empty<string>(); _catalogError = ErrorText(inner);
        }
        _catalogTask = null;
        if (_open && _screen == "catalog") Render();
    }
    private static void RememberTabLayouts()
    {
        foreach (var tab in _view!._tabItems)
        {
            var rect = tab.GetComponent<RectTransform>();
            var min = rect.anchorMin; var max = rect.anchorMax; var pivot = rect.pivot;
            var size = rect.sizeDelta; var position = rect.anchoredPosition;
            var layout = tab.GetComponent<LayoutElement>();
            var preferred = layout != null ? layout.preferredHeight : -1f;
            var minimum = layout != null ? layout.minHeight : -1f;
            var flexible = layout != null ? layout.flexibleHeight : -1f;
            var added = layout == null;
            layout ??= tab.gameObject.AddComponent<LayoutElement>();
            RestoreTabLayouts.Add(() =>
            {
                if (rect == null) return;
                rect.anchorMin = min; rect.anchorMax = max; rect.pivot = pivot;
                rect.sizeDelta = size; rect.anchoredPosition = position;
                if (added) UnityEngine.Object.Destroy(layout);
                else { layout.preferredHeight = preferred; layout.minHeight = minimum; layout.flexibleHeight = flexible; }
            });
        }
    }
    private static void FitTabs()
    {
        // Fit all native tabs plus AI, including the official layout's spacing and padding.
        var container = _view!._tabItemContainer.GetComponent<RectTransform>();
        if (container == null || container.rect.height <= 0) return;
        var tabs = _view._tabItems.ToArray().Concat(new[] { _tab! }).ToArray();
        var group = container.GetComponent<VerticalLayoutGroup>();
        var spacing = group != null ? group.spacing : 4f;
        var padding = group != null ? group.padding.vertical : 0;
        var height = Math.Max(1f, (container.rect.height - padding - spacing * (tabs.Length - 1)) / tabs.Length);
        for (var i = 0; i < tabs.Length; i++)
        {
            var rect = tabs[i].GetComponent<RectTransform>();
            var layout = tabs[i].GetComponent<LayoutElement>() ?? tabs[i].gameObject.AddComponent<LayoutElement>();
            layout.minHeight = height; layout.preferredHeight = height; layout.flexibleHeight = 0;
            rect.sizeDelta = new Vector2(rect.sizeDelta.x, height);
            if (group == null)
            {
                rect.anchorMin = new Vector2(0, 1); rect.anchorMax = new Vector2(1, 1); rect.pivot = new Vector2(.5f, 1);
                rect.sizeDelta = new Vector2(0, height); rect.anchoredPosition = new Vector2(0, -i * (height + spacing));
            }
        }
    }
    private static void Open()
    {
        if (_open || _view == null || !_view.IsVisible) return;
        DialogueManagerUpdatePatch.CancelSettingsKeyBinding("AI settings opened");
        _error = ""; InvalidInputs.Clear();
        try { _draft = new AiSettingsDraft(Plugin.AiConfig); }
        catch (Exception e) { _draft = null; _error = ErrorText(e); }
        _nativeRootActive = _view._settingItemRoot.gameObject.activeSelf;
        _view._settingItemRoot.gameObject.SetActive(false);
        foreach (var tab in _view._tabItems) { tab.SetSelected(false); tab._image.sprite = tab._unselectedSprite; }
        UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(null);
        var source = _view._settingItemRoot.GetComponent<RectTransform>();
        _pageRoot = new GameObject("LilithAiSettings", Il2CppType.Of<RectTransform>());
        _pageRoot.transform.SetParent(_view._settingItemRoot.parent, false);
        var rect = _pageRoot.GetComponent<RectTransform>();
        rect.anchorMin = source.anchorMin; rect.anchorMax = source.anchorMax;
        rect.pivot = source.pivot; rect.sizeDelta = source.sizeDelta; rect.anchoredPosition = source.anchoredPosition;
        rect.localScale = source.localScale; rect.localRotation = source.localRotation;
        var pageLayout = _pageRoot.AddComponent<LayoutElement>(); pageLayout.ignoreLayout = true;
        _open = true; _screen = "current"; _page = 0;
        Render();
    }
    internal static void OfficialTabSelected(TraySettingNewView view) { if (_view == view) Close(); }
    internal static void Close()
    {
        if (!_open) return;
        DisarmKeyboard();
        foreach (var input in _pageRoot!.GetComponentsInChildren<TMP_InputField>(true)) { input.DeactivateInputField(); input.SetTextWithoutNotify(""); }
        ClearRows();
        UnityEngine.Object.Destroy(_pageRoot); _pageRoot = null;
        _draft = null; _pick = null; _open = false; InvalidInputs.Clear();
        _catalogCts?.Cancel(); _catalogTask = null; _catalogIds = null; _catalogError = ""; _catalogQuery = ""; CatalogPicked.Clear();
        _formNew = false; _formReturn = "providers"; _newProviderId = ""; _newProviderName = ""; _newBaseUrl = ""; _newKey = ""; _newEnvName = "";
        if (_view != null)
        {
            _view._settingItemRoot.gameObject.SetActive(_nativeRootActive);
            foreach (var tab in _view._tabItems) { tab._image.overrideSprite = null; tab.SetSelected(tab.Tab == _view._currentTab); }
        }
        if (_tab != null) _tab._image.sprite = _tab._unselectedSprite;
    }
    internal static void Reset()
    {
        Close();
        if (_tab != null) UnityEngine.Object.Destroy(_tab.gameObject);
        foreach (var restore in RestoreTabLayouts) restore();
        RestoreTabLayouts.Clear();
        _tab = null; _view = null;
    }
    private static void ClearRows()
    {
        // Destroying a focused field does not reliably run its deselect callback.
        DisarmKeyboard();
        foreach (var row in Rows) { row.SetActive(false); UnityEngine.Object.Destroy(row); }
        Rows.Clear(); _save = null; _status = null;
    }
    private static void Navigate(string screen)
    {
        // Leaving the create form keeps a valid provider in the draft. The chat protocol list is part of that form.
        if (_formNew && InNewConnection(_screen) && !InNewConnection(screen))
        {
            if (NewFormPending() && !TryCommitNewProvider())
            {
                var home = _screen == "asr-form" ? "asr-form" : "form";
                if (_screen != home) { _screen = home; _page = 0; Render(); }
                else if (_status != null) _status.text = _error;
                return;
            }
            _formNew = false;
        }
        _screen = screen; _page = 0; _error = InvalidInputs.Count > 0 ? NumericError() : "";
        Render();
    }
    private static bool InNewConnection(string screen) => screen == "form" || screen == "protocol" || screen == "asr-form";
    private static void CancelNewProvider()
    {
        var back = _formReturn == "asr" ? "asr" : "providers";
        _formNew = false; _formReturn = "providers"; _newProviderId = ""; _newProviderName = ""; _newBaseUrl = ""; _newKey = ""; _newEnvName = "";
        _newProtocol = "chat-completions"; _newCredentialMode = "stored"; _error = "";
        Navigate(back);
    }
    private static string NumericError() => T("請修正進階參數中的無效數字後再儲存。", "请修正高级参数中的无效数字后再保存。", "詳細設定の数値を修正してから保存してください。", "Correct invalid numbers in generation parameters before saving.");
    private static void DropInvalid(string prefix) { foreach (var key in InvalidInputs.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToArray()) InvalidInputs.Remove(key); }
    private static void Save()
    {
        if (_draft == null || InvalidInputs.Count > 0 || !DialogueManagerUpdatePatch.CanChangeAiSettings) return;
        var committed = false;
        if (_formNew && InNewConnection(_screen) && NewFormPending())
        {
            if (!TryCommitNewProvider()) { if (_status != null) _status.text = _error; return; }
            committed = true;
        }
        try
        {
            _draft.Save(Plugin.AiConfig);
            DialogueManagerUpdatePatch.ChatClient.InvalidateCapabilities();
            _error = T("已儲存，下次操作生效。", "已保存，下次操作生效。", "保存しました。次の操作から適用。", "Saved. Applies to the next operation.");
        }
        catch (Exception e) { _error = ErrorText(e); }
        if (committed) Render();
        else if (_status != null) _status.text = _error;
    }
    private static void Render()
    {
        if (!_open) return;
        ClearRows();
        ButtonAt(T("目前模型", "当前模型", "使用モデル", "Active models"), () => Navigate("current"), .00f, .32f, .90f, .99f);
        ButtonAt(T("提供商與聊天", "提供商与聊天", "接続・チャット", "Providers / chat"), () => Navigate("providers"), .34f, .66f, .90f, .99f);
        ButtonAt(T("語音識別", "语音识别", "音声認識", "Speech recognition"), () => Navigate("asr"), .68f, 1f, .90f, .99f);
        _row = 0;
        if (_draft != null)
        {
            switch (_screen)
            {
                case "current": Current(); break;
                case "providers": Providers(); break;
                case "form": Form(); break;
                case "models": ChatModels(); break;
                case "catalog": Catalog(); break;
                case "model": ChatModel(); break;
                case "advanced": Advanced(); break;
                case "asr": AsrProviders(); break;
                case "asr-form": AsrForm(); break;
                case "asrs": AsrModels(); break;
                case "asr-model": AsrModel(); break;
                case "asr-protocol": AsrProtocolPicker(); break;
                case "picker": Picker(); break;
                case "protocol": ProtocolPicker(); break;
            }
        }
        _status = LabelAt(_error, .00f, 1f, .09f, .17f);
        ButtonAt(T("取消", "取消", "キャンセル", "Cancel"), Close, .34f, .65f, .00f, .08f);
        _save = ButtonAt(T("儲存全部", "保存全部", "すべて保存", "Save all"), Save, .68f, 1f, .00f, .08f);
        _save.interactable = _draft != null && InvalidInputs.Count == 0 && DialogueManagerUpdatePatch.CanChangeAiSettings;
    }
    private static AiConfiguration C => _draft!.Config;
    private static AiProvider P => C.Providers[_providerId];
    private static AiAsrProvider A => C.AsrProviders[_providerId];
    private static string RefName(AiModelReference? r, bool chat)
    {
        if (r == null) return T("無", "无", "なし", "None");
        var invalid = T("無效引用", "无效引用", "無効な参照", "Invalid reference");
        if (chat)
        {
            if (!C.Providers.TryGetValue(r.ProviderId, out var p)) return invalid;
            var name = p.ChatModels.TryGetValue(r.ModelId, out var m) ? m.Name : null;
            return p.Name + " · " + (string.IsNullOrWhiteSpace(name) ? r.ModelId : name);
        }
        if (!C.AsrProviders.TryGetValue(r.ProviderId, out var speech)) return invalid;
        var speechName = speech.Models.TryGetValue(r.ModelId, out var model) ? model.Name : null;
        return speech.Name + " · " + (string.IsNullOrWhiteSpace(speechName) ? r.ModelId : speechName);
    }
    private static void Current()
    {
        RowButton(T("聊天：", "聊天：", "チャット：", "Chat: ") + RefName(C.Chat, true), () => Pick(true, false, r => C.Chat = r!));
        RowButton("ASR: " + RefName(C.Asr, false), () => Pick(false, false, r => C.Asr = r!));
        RowButton(T("ASR 回退：", "ASR 回退：", "ASRフォールバック：", "ASR fallback: ") + RefName(C.AsrFallback, false), () => Pick(false, true, r => C.AsrFallback = r));
        try
        {
            var p = C.Providers[C.Chat.ProviderId]; var m = p.ChatModels[C.Chat.ModelId];
            var s = new AiChatSnapshot(C.Chat.ProviderId, C.Chat.ModelId, p.BaseUrl, AiConfigurationStore.ResolveKey(p.Credential, _draft!.Credentials), m);
            var capability = DialogueManagerUpdatePatch.ChatClient.Capability(s);
            RowLabel(T("本機工具狀態：", "本机工具状态：", "ローカルツール：", "Local tools: ") + CapabilityState(capability.State) + (capability.Reason.Length == 0 ? "" : " · " + T("服務明確拒絕工具參數", "服务明确拒绝工具参数", "サービスがツール指定を拒否", "Service rejected tool parameters")));
            RowButton(T("編輯聊天模型／工具策略", "编辑聊天模型／工具策略", "モデル・ツール方針を編集", "Edit chat model / tool policy"), () => { _formNew = false; _providerId = C.Chat.ProviderId; _modelId = C.Chat.ModelId; _modelReturn = "current"; Navigate("model"); });
        }
        catch (Exception e) { RowLabel(ErrorText(e)); }
        RowLabel(T("編輯只保留在草稿中；切換官方分頁會丟棄。", "编辑只保留在草稿中；切换官方分页会丢弃。", "編集は下書きです。公式タブへ移ると破棄します。", "Unsaved edits are discarded on official tab selection or close."));
    }
    private static string CapabilityState(string state) => state switch
    {
        "supported" => T("已支援", "已支持", "対応確認済み", "Supported"),
        "unsupported" => T("不支援", "不支持", "非対応", "Unsupported"),
        _ => T("尚未確認", "尚未确认", "未確認", "Unknown")
    };
    // Chat setup follows the custom model API card: connection, then models, then one model.
    // Eight content rows fit above the status line, so each step stays on one screen.
    private static void Providers()
    {
        RowButton(T("新增自訂模型 API", "添加自定义模型 API", "カスタムモデル API を追加", "Add custom model API"), BeginNewProvider);
        foreach (var entry in Slice(C.Providers.ToArray()))
        {
            var id = entry.Key;
            var protocol = string.IsNullOrWhiteSpace(entry.Value.Protocol) ? "" : " · " + ProtocolLabel(entry.Value.Protocol);
            RowButton(entry.Value.Name + " · " + id + protocol, () => { _formNew = false; _formReturn = "providers"; _providerId = id; Navigate("form"); });
        }
        Pagination(C.Providers.Count);
    }
    private static void BeginNewProvider()
    {
        _formNew = true; _formReturn = "providers"; _newProviderId = ""; _newProviderName = ""; _newBaseUrl = "";
        _newProtocol = "chat-completions"; _newCredentialMode = "stored"; _newEnvName = ""; _newKey = "";
        Navigate("form");
    }
    private static void Form()
    {
        if (_formNew)
        {
            InputRow("Provider ID", _newProviderId, v => _newProviderId = v, false, "my-gateway");
            InputRow(T("顯示名稱", "显示名称", "表示名", "Display name"), _newProviderName, v => _newProviderName = v);
            InputRow(T("API 位址", "API 地址", "API アドレス", "API address"), _newBaseUrl, v => _newBaseUrl = v, false, "https://example.com/v1");
            RowButton(ProtocolButton(_newProtocol), OpenProtocol);
            CredentialOptions(_newCredentialMode, v => { _newCredentialMode = v; Render(); });
            if (_newCredentialMode == "stored") InputRow("API Key", _newKey, v => _newKey = v, true);
            else if (_newCredentialMode == "env") InputRow(T("環境變數名稱", "环境变量名称", "環境変数名", "Environment name"), _newEnvName, v => _newEnvName = v);
            RowLabel(T("ID 建立後不可改。一個提供商只用一種聊天協定。", "ID 创建后不可改。一个提供商只用一种聊天协议。", "IDは作成後に変更できません。接続ごとにプロトコルは一つです。", "The ID is permanent. A provider uses one chat protocol."));
            RowButtons(T("返回", "返回", "戻る", "Back"), CancelNewProvider, T("下一步：模型", "下一步：模型", "次へ：モデル", "Next: models"), ContinueToModels);
            return;
        }
        var customRef = IsCustomCredential(P.Credential, _providerId);
        var showClear = P.Credential.Mode == "stored" && HasStoredKey(P.Credential, _providerId);
        if (!customRef) RowLabel("Provider ID · " + _providerId + (showClear ? T("（不可改）", "（不可改）", "（変更不可）", " (permanent)") : ""));
        InputRow(T("顯示名稱", "显示名称", "表示名", "Display name"), P.Name, v => P.Name = v);
        InputRow(T("API 位址", "API 地址", "API アドレス", "API address"), P.BaseUrl, v => P.BaseUrl = v, false, "https://example.com/v1");
        RowButton(ProtocolButton(P.Protocol), OpenProtocol);
        CredentialOptions(P.Credential.Mode, v =>
        {
            P.Credential.Mode = v;
            if (v != "none" && string.IsNullOrWhiteSpace(P.Credential.Reference)) P.Credential.Reference = _providerId;
            Render();
        });
        if (P.Credential.Mode == "env") InputRow(T("環境變數名稱", "环境变量名称", "環境変数名", "Environment name"), P.Credential.Reference ?? "", v => P.Credential.Reference = v);
        else if (P.Credential.Mode == "stored")
        {
            if (customRef) InputRow(T("憑據 ID", "凭据 ID", "Credential ID", "Credential ID"), P.Credential.Reference ?? "", v => P.Credential.Reference = v);
            var configured = HasStoredKey(P.Credential, _providerId);
            InputRow("API Key", "", v => { if (!string.IsNullOrWhiteSpace(v)) _draft!.Credentials[CredentialReference(P.Credential, _providerId)] = v.Trim(); }, true,
                configured ? T("已配置；留空不修改", "已配置；留空不修改", "設定済み・空欄で維持", "Configured; blank preserves key") : T("未配置", "未配置", "未設定", "Not configured"));
            if (showClear) RowButton(T("明確清除已存 Key", "明确清除已存 Key", "保存キーを削除", "Clear stored key"), () => ClearStoredKey(P.Credential, _providerId));
        }
        if (!showClear) RowLabel(T("ID 建立後不可改。一個提供商只用一種聊天協定。", "ID 创建后不可改。一个提供商只用一种聊天协议。", "IDは作成後に変更できません。接続ごとにプロトコルは一つです。", "The ID is permanent. A provider uses one chat protocol."));
        RowButtons(T("刪除提供商", "删除提供商", "接続を削除", "Delete provider"), DeleteProvider, T("下一步：模型", "下一步：模型", "次へ：モデル", "Next: models"), ContinueToModels);
    }
    private static string ProtocolLabel(string protocol) => AiConfigurationStore.CanonicalChatProtocol(protocol) switch
    {
        "chat-completions" => "Chat", "responses" => "Responses", "anthropic" => "Anthropic", "gemini" => "Gemini", _ => protocol
    };
    private static string ProtocolTitle(string protocol) => AiConfigurationStore.CanonicalChatProtocol(protocol) switch
    {
        "chat-completions" => "OpenAI Chat Completions",
        "responses" => "OpenAI Responses",
        "anthropic" => "Anthropic Messages",
        "gemini" => "Gemini",
        _ => protocol
    };
    private static string ProtocolButton(string protocol)
    {
        var shown = string.IsNullOrWhiteSpace(protocol) ? T("未選擇", "未选择", "未選択", "Not selected") : ProtocolTitle(protocol);
        return T("API 協定：", "API 协议：", "API プロトコル：", "API protocol: ") + shown;
    }
    private static void OpenProtocol() => Navigate("protocol");
    private static void ProtocolPicker()
    {
        var current = AiConfigurationStore.CanonicalChatProtocol(_formNew ? _newProtocol : P.Protocol);
        RowButton(T("返回", "返回", "戻る", "Back"), () => Navigate("form"));
        foreach (var protocol in AiConfigurationStore.ChatProtocols)
        {
            var chosen = protocol == current;
            var label = ProtocolTitle(protocol) + (chosen ? T("（目前）", "（当前）", "（選択中）", " (selected)") : "");
            var value = protocol;
            RowButton(label, () =>
            {
                if (_formNew) _newProtocol = value;
                else SetProviderProtocol(value);
                Navigate("form");
            });
        }
    }
    private static void SetProviderProtocol(string protocol)
    {
        foreach (var id in P.ChatModels.Keys) DropInvalid(_providerId + "/" + id + "/Thinking ");
        P.Protocol = protocol;
        if (protocol != "chat-completions") P.MaxTokensField = null;
        foreach (var model in P.ChatModels.Values)
        {
            model.Protocol = protocol; model.ReasoningEffort = null; model.ThinkingBudget = null; model.ThinkingLevel = null; model.NativeSearch = "none";
            model.MaxTokensField = protocol == "chat-completions" ? P.MaxTokensField : null;
        }
        _error = InvalidInputs.Count > 0 ? NumericError() : "";
    }
    private static void CredentialOptions(string current, Action<string> changed) => Options(T("憑據", "凭据", "認証", "Credential"), new[] { "stored", "env", "none" }, current, changed,
        new[] { T("本機", "本地", "ローカル", "Local"), T("環境變數", "环境变量", "環境変数", "Environment"), T("無", "无", "なし", "None") });
    private static bool IsCustomCredential(AiCredential credential, string defaultReference)
    {
        if (credential.Mode != "stored") return false;
        var reference = credential.Reference ?? "";
        return reference.Length > 0 && !string.Equals(reference, defaultReference, StringComparison.Ordinal);
    }
    private static string CredentialReference(AiCredential credential, string defaultReference)
    {
        var reference = credential.Reference;
        return string.IsNullOrWhiteSpace(reference) ? defaultReference : reference;
    }
    private static bool HasStoredKey(AiCredential credential, string defaultReference) => _draft!.Credentials.ContainsKey(CredentialReference(credential, defaultReference));
    private static void ClearStoredKey(AiCredential credential, string defaultReference) { _draft!.Credentials.Remove(CredentialReference(credential, defaultReference)); Render(); }
    private static void DeleteProvider()
    {
        if (_screen == "asr-form") C.AsrProviders.Remove(_providerId);
        else { C.Providers.Remove(_providerId); DropInvalid(_providerId + "/"); }
        var back = _screen == "asr-form" ? "asr" : "providers";
        _formNew = false; _formReturn = "providers"; Navigate(back);
    }
    private static bool NewFormPending()
    {
        var protocolPending = _formReturn == "asr" ? _newProtocol.Length > 0 : _newProtocol != "chat-completions";
        return _newProviderId.Length > 0 || _newProviderName.Length > 0 || _newBaseUrl.Length > 0 || _newKey.Length > 0 || _newEnvName.Length > 0 || protocolPending || _newCredentialMode != "stored";
    }
    private static bool TryCommitNewProvider()
    {
        if (!_formNew) return true;
        var id = _newProviderId.Trim();
        var name = _newProviderName.Trim();
        if (!AiConfigurationStore.IsProviderId(id, true))
            _error = T("ID 須為小寫字母開頭，只能含小寫、數字和連字號。", "ID 须为小写字母开头，只能含小写、数字和连字符。", "IDは小文字で始め、小文字・数字・ハイフンのみです。", "Provider ID must be lowercase and use letters, digits, and hyphens.");
        else if (name.Length == 0)
            _error = T("請填寫顯示名稱。", "请填写显示名称。", "表示名を入力してください。", "Enter a display name.");
        else if (_formReturn == "asr" ? C.AsrProviders.ContainsKey(id) : C.Providers.ContainsKey(id))
            _error = T("這個 Provider ID 已存在。", "这个 Provider ID 已存在。", "この Provider ID は既にあります。", "That Provider ID already exists.");
        else
        {
            var speech = _formReturn == "asr";
            var mode = _newCredentialMode;
            var storedReference = speech ? AiConfigurationStore.AsrCredentialReference(id) : id;
            string? reference = mode == "stored" ? storedReference : mode == "env" && !string.IsNullOrWhiteSpace(_newEnvName) ? _newEnvName.Trim() : null;
            var credential = new AiCredential { Mode = mode, Reference = reference };
            if (speech) C.AsrProviders[id] = new AiAsrProvider { Name = name, BaseUrl = _newBaseUrl.Trim(), Credential = credential };
            else C.Providers[id] = new AiProvider { Name = name, BaseUrl = _newBaseUrl.Trim(), Protocol = _newProtocol, Credential = credential };
            if (mode == "stored" && !string.IsNullOrWhiteSpace(_newKey)) _draft!.Credentials[storedReference] = _newKey.Trim();
            _providerId = id; _formNew = false; _newProviderId = ""; _newProviderName = ""; _newBaseUrl = ""; _newKey = ""; _newEnvName = "";
            _newProtocol = "chat-completions"; _newCredentialMode = "stored"; _error = "";
            return true;
        }
        return false;
    }
    private static void ContinueToModels()
    {
        if (_formNew)
        {
            if (!TryCommitNewProvider()) { if (_status != null) _status.text = _error; return; }
        }
        else if (string.IsNullOrWhiteSpace(P.Protocol))
        {
            _error = T("請選擇 API 協定。", "请选择 API 协议。", "API プロトコルを選択してください。", "Select an API protocol.");
            if (_status != null) _status.text = _error; return;
        }
        Navigate("models");
    }
    private static void BeginCatalog()
    {
        _catalogCts?.Cancel(); _catalogCts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        _catalogIds = null; _catalogError = ""; _catalogQuery = ""; CatalogPicked.Clear();
        string? key;
        try { key = AiConfigurationStore.ResolveKey(P.Credential, _draft!.Credentials); }
        catch (Exception e) { _catalogError = ErrorText(e); Navigate("catalog"); return; }
        _catalogTask = DialogueManagerUpdatePatch.FetchChatModelsAsync(P.Protocol, P.BaseUrl, key, _catalogCts.Token);
        Navigate("catalog");
    }
    private static void Catalog()
    {
        if (_catalogTask != null)
        {
            RowButton(T("返回模型", "返回模型", "モデルへ戻る", "Back to models"), () => Navigate("models"));
            RowLabel(T("正在取得模型目錄……", "正在获取模型目录……", "モデル一覧を取得しています……", "Fetching the model catalog..."));
            return;
        }
        if (_catalogError.Length > 0)
        {
            RowButton(T("返回模型", "返回模型", "モデルへ戻る", "Back to models"), () => Navigate("models"));
            RowLabel(_catalogError);
            return;
        }
        InputRow(T("搜尋", "搜索", "検索", "Search"), _catalogQuery, v => _catalogQuery = v, false, T("輸入後點篩選", "输入后点筛选", "入力後に絞り込み", "Type, then filter"));
        var y = NextBottom();
        ButtonAt(T("添加所選", "添加所选", "選択を追加", "Add selected") + " " + CatalogPicked.Count, AddSelectedModels, 0f, .48f, y, y + .075f);
        ButtonAt(T("篩選", "筛选", "絞り込み", "Filter"), () => { _page = 0; Render(); }, .50f, .73f, y, y + .075f);
        ButtonAt(T("返回", "返回", "戻る", "Back"), () => Navigate("models"), .75f, 1f, y, y + .075f);
        var existing = new HashSet<string>(P.ChatModels.Values.Select(m => m.Model), StringComparer.Ordinal);
        var ids = (_catalogIds ?? Array.Empty<string>()).Where(id => !existing.Contains(id)).ToArray();
        var query = _catalogQuery.Trim();
        var filtered = query.Length == 0 ? ids : ids.Where(id => id.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (ids.Length == 0) RowLabel(T("沒有可新增的模型。", "没有可新增的模型。", "追加できるモデルがありません。", "No new models to add."));
        else if (filtered.Length == 0) RowLabel(T("沒有符合的模型。", "没有符合的模型。", "一致するモデルがありません。", "No matching models."));
        foreach (var id in Slice(filtered))
        {
            var picked = CatalogPicked.Contains(id);
            var prefix = picked ? T("已選 · ", "已选 · ", "選択中 · ", "Selected · ") : T("選擇 · ", "选择 · ", "選択 · ", "Select · ");
            RowButton(prefix + id, () =>
            {
                if (!CatalogPicked.Add(id)) CatalogPicked.Remove(id);
                _error = InvalidInputs.Count > 0 ? NumericError() : "";
                Render();
            });
        }
        Pagination(filtered.Length);
    }
    private static void AddSelectedModels()
    {
        if (CatalogPicked.Count == 0)
        {
            _error = T("請先勾選要添加的模型。", "请先勾选要添加的模型。", "追加するモデルを選択してください。", "Tick models to add.");
            if (_status != null) _status.text = _error; return;
        }
        foreach (var id in CatalogPicked) AiConfigurationStore.AddChatModel(P, id);
        CatalogPicked.Clear();
        Navigate("models");
    }
    private static void ChatModels()
    {
        RowButtons(T("返回連接", "返回连接", "接続へ戻る", "Back to connection"), () => Navigate("form"), T("手動新增", "手动添加", "手入力で追加", "Add manually"), AddChatModelManually);
        RowButton(T("取得可用模型", "获取可用模型", "利用可能なモデルを取得", "Fetch available models"), BeginCatalog);
        var entries = P.ChatModels.Select(e => (e.Key, e.Value.Name, e.Value.Model)).ToArray();
        foreach (var (id, name, model) in Slice(entries))
            RowButton(ChatModelLabel(name, model), () => { _modelId = id; _modelReturn = "models"; Navigate("model"); });
        Pagination(entries.Length);
    }
    private static string ChatModelLabel(string name, string model)
    {
        if (string.IsNullOrWhiteSpace(model)) return T("未填寫模型標識", "未填写模型标识", "モデル名が未入力", "Upstream model is empty");
        return string.IsNullOrWhiteSpace(name) || name == model ? model : name + " · " + model;
    }
    private static void AddChatModelManually()
    {
        var protocol = AiConfigurationStore.CanonicalChatProtocol(P.Protocol);
        P.Protocol = protocol;
        _modelId = "model-" + Guid.NewGuid().ToString("N")[..8];
        P.ChatModels[_modelId] = new AiChatModel { Protocol = protocol, MaxTokensField = protocol == "chat-completions" ? P.MaxTokensField : null };
        _modelReturn = "models";
        Navigate("model");
    }
    private static void ChatModel()
    {
        var m = P.ChatModels[_modelId];
        InputRow(T("顯示名稱", "显示名称", "表示名", "Display name"), m.Name, v => m.Name = v);
        InputRow(T("模型標識", "模型标识", "モデル名", "Upstream model"), m.Model, v => m.Model = v);
        InputRow(T("上下文視窗（可選）", "上下文窗口（可选）", "コンテキスト（任意）", "Context window (optional)"), m.ContextWindow?.ToString(CultureInfo.InvariantCulture) ?? "", v => m.ContextWindow = OptionalInt(v));
        InputRow(T("輸出上限（可選）", "输出上限（可选）", "出力上限（任意）", "Output limit (optional)"), m.MaxOutputTokens?.ToString(CultureInfo.InvariantCulture) ?? "", v => m.MaxOutputTokens = OptionalInt(v));
        ToolOptions(m);
        RowButton(T("進階生成參數／原生搜尋", "高级生成参数／原生搜索", "生成パラメータ・検索", "Generation parameters / native search"), () => Navigate("advanced"));
        RowLabel(T("上下文視窗只作記錄。空白輸出上限用服務端預設；Anthropic 必填。", "上下文窗口只作记录。空白输出上限用服务端默认；Anthropic 必填。", "コンテキストは記録のみ。空欄の出力上限はサーバー既定。Anthropicは必須。", "Context window is stored only. A blank output limit uses the server default. Anthropic requires one."));
        RowButtons(T("返回", "返回", "戻る", "Back"), () => Navigate(_modelReturn), T("刪除模型", "删除模型", "モデルを削除", "Delete model"), () => { P.ChatModels.Remove(_modelId); DropInvalid(_providerId + "/" + _modelId + "/"); Navigate(_modelReturn); });
    }
    private static void ToolOptions(AiChatModel m) => Options(T("本機工具策略", "本机工具策略", "ローカルツール方針", "Local tool policy"), new[] { "auto", "on", "off" }, m.Tools, v => m.Tools = v);
    private static void Advanced()
    {
        var m = P.ChatModels[_modelId];
        if (m.Protocol == "chat-completions") Options(T("上限欄位", "上限字段", "上限フィールド", "Output field"), new[] { "max_completion_tokens", "max_tokens" }, P.MaxTokensField ?? "max_completion_tokens", v =>
        {
            P.MaxTokensField = v == "max_completion_tokens" ? null : v;
            foreach (var model in P.ChatModels.Values) model.MaxTokensField = P.MaxTokensField;
        }, new[] { "completion", "max_tokens" });
        InputRow(T("溫度（可選）", "温度（可选）", "温度（任意）", "Temperature (optional)"), m.Temperature?.ToString(CultureInfo.InvariantCulture) ?? "", v => m.Temperature = string.IsNullOrWhiteSpace(v) ? null : double.Parse(v, CultureInfo.InvariantCulture));
        if (m.Protocol == "responses" || m.Protocol == "chat-completions") InputRow("Reasoning effort", m.ReasoningEffort ?? "", v => m.ReasoningEffort = Blank(v), false, "off, low, medium, high, max");
        if (m.Protocol == "anthropic" || m.Protocol == "gemini") InputRow("Thinking budget", m.ThinkingBudget?.ToString(CultureInfo.InvariantCulture) ?? "", v => m.ThinkingBudget = OptionalInt(v));
        if (m.Protocol == "gemini") InputRow("Thinking level", m.ThinkingLevel ?? "", v => m.ThinkingLevel = Blank(v));
        if (m.Protocol == "gemini" || m.Protocol == "responses") Switch(T("原生联网搜尋", "原生联网搜索", "ネイティブ検索", "Native web search"), m.NativeSearch != "none", v => m.NativeSearch = v ? m.Protocol == "gemini" ? "google" : "dashscope" : "none");
        RowButton(T("返回模型", "返回模型", "モデルへ戻る", "Back to model"), () => Navigate("model"));
    }
    private static string? Blank(string v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
    private static int? OptionalInt(string v) => string.IsNullOrWhiteSpace(v) ? null : int.Parse(v, CultureInfo.InvariantCulture);
    // Speech setup uses the same three steps as chat, against its own provider list. The speech protocol stays on each model.
    private static void AsrProviders()
    {
        RowButton(T("新增語音模型 API", "添加语音模型 API", "音声モデル API を追加", "Add speech model API"), BeginNewAsrProvider);
        foreach (var entry in Slice(C.AsrProviders.ToArray()))
        {
            var id = entry.Key;
            var protocols = entry.Value.Models.Values.Select(m => m.Protocol).Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.Ordinal).ToArray();
            var suffix = protocols.Length == 1 ? " · " + AsrProtocolLabel(protocols[0]) : protocols.Length > 1 ? T(" · 多種", " · 多种", " · 複数", " · mixed") : "";
            RowButton(entry.Value.Name + " · " + id + suffix, () => { _formNew = false; _formReturn = "asr"; _providerId = id; Navigate("asr-form"); });
        }
        Pagination(C.AsrProviders.Count);
    }
    private static void BeginNewAsrProvider()
    {
        _formNew = true; _formReturn = "asr"; _newProviderId = ""; _newProviderName = ""; _newBaseUrl = "";
        _newProtocol = ""; _newCredentialMode = "stored"; _newEnvName = ""; _newKey = "";
        Navigate("asr-form");
    }
    private static void AsrForm()
    {
        var note = T("位址與憑據只屬於語音識別。語音協定在模型上選擇。", "地址和凭据只属于语音识别。语音协议在模型上选择。", "アドレスと認証は音声認識専用です。音声プロトコルはモデルで選びます。", "Address and credential belong only to speech recognition. Choose the speech protocol on the model.");
        if (_formNew)
        {
            InputRow("Provider ID", _newProviderId, v => _newProviderId = v, false, "my-gateway");
            InputRow(T("顯示名稱", "显示名称", "表示名", "Display name"), _newProviderName, v => _newProviderName = v);
            InputRow(T("API 位址", "API 地址", "API アドレス", "API address"), _newBaseUrl, v => _newBaseUrl = v, false, "https://example.com/v1");
            CredentialOptions(_newCredentialMode, v => { _newCredentialMode = v; Render(); });
            if (_newCredentialMode == "stored") InputRow("API Key", _newKey, v => _newKey = v, true);
            else if (_newCredentialMode == "env") InputRow(T("環境變數名稱", "环境变量名称", "環境変数名", "Environment name"), _newEnvName, v => _newEnvName = v);
            RowLabel(T("ID 建立後不可改。位址與憑據只屬於語音識別。", "ID 创建后不可改。地址和凭据只属于语音识别。", "IDは作成後に変更できません。アドレスと認証は音声認識専用です。", "The ID is permanent. Address and credential belong only to speech recognition."));
            RowButtons(T("返回", "返回", "戻る", "Back"), CancelNewProvider, T("下一步：模型", "下一步：模型", "次へ：モデル", "Next: models"), ContinueToAsrModels);
            return;
        }
        var speechRef = AiConfigurationStore.AsrCredentialReference(_providerId);
        var customRef = IsCustomCredential(A.Credential, speechRef);
        var showClear = A.Credential.Mode == "stored" && HasStoredKey(A.Credential, speechRef);
        if (!customRef) RowLabel("Provider ID · " + _providerId + (showClear ? T("（不可改）", "（不可改）", "（変更不可）", " (permanent)") : ""));
        InputRow(T("顯示名稱", "显示名称", "表示名", "Display name"), A.Name, v => A.Name = v);
        InputRow(T("API 位址", "API 地址", "API アドレス", "API address"), A.BaseUrl, v => A.BaseUrl = v, false, "https://example.com/v1");
        CredentialOptions(A.Credential.Mode, v =>
        {
            A.Credential.Mode = v;
            if (v != "none" && string.IsNullOrWhiteSpace(A.Credential.Reference)) A.Credential.Reference = speechRef;
            Render();
        });
        if (A.Credential.Mode == "env") InputRow(T("環境變數名稱", "环境变量名称", "環境変数名", "Environment name"), A.Credential.Reference ?? "", v => A.Credential.Reference = v);
        else if (A.Credential.Mode == "stored")
        {
            if (customRef) InputRow(T("憑據 ID", "凭据 ID", "Credential ID", "Credential ID"), A.Credential.Reference ?? "", v => A.Credential.Reference = v);
            var configured = HasStoredKey(A.Credential, speechRef);
            InputRow("API Key", "", v => { if (!string.IsNullOrWhiteSpace(v)) _draft!.Credentials[CredentialReference(A.Credential, speechRef)] = v.Trim(); }, true,
                configured ? T("已配置；留空不修改", "已配置；留空不修改", "設定済み・空欄で維持", "Configured; blank preserves key") : T("未配置", "未配置", "未設定", "Not configured"));
            if (showClear) RowButton(T("明確清除已存 Key", "明确清除已存 Key", "保存キーを削除", "Clear stored key"), () => ClearStoredKey(A.Credential, speechRef));
        }
        RowLabel(note);
        RowButtons(T("刪除提供商", "删除提供商", "接続を削除", "Delete provider"), DeleteProvider, T("下一步：模型", "下一步：模型", "次へ：モデル", "Next: models"), ContinueToAsrModels);
    }
    private static void ContinueToAsrModels()
    {
        if (_formNew && !TryCommitNewProvider()) { if (_status != null) _status.text = _error; return; }
        Navigate("asrs");
    }
    private static void AsrModels()
    {
        RowButtons(T("返回連接", "返回连接", "接続へ戻る", "Back to connection"), () => Navigate("asr-form"), T("手動新增", "手动添加", "手入力で追加", "Add manually"), AddAsrModelManually);
        var entries = A.Models.Select(e => (e.Key, e.Value.Name, e.Value.Model, e.Value.Protocol)).ToArray();
        foreach (var (id, name, model, protocol) in Slice(entries))
            RowButton(ChatModelLabel(name, model) + " · " + AsrProtocolLabel(protocol), () => { _modelId = id; Navigate("asr-model"); });
        Pagination(entries.Length);
    }
    private static void AddAsrModelManually()
    {
        _modelId = "model-" + Guid.NewGuid().ToString("N")[..8];
        A.Models[_modelId] = new AiAsrModel();
        Navigate("asr-model");
    }
    private static void AsrModel()
    {
        var m = A.Models[_modelId];
        InputRow(T("顯示名稱", "显示名称", "表示名", "Display name"), m.Name, v => m.Name = v);
        InputRow(T("模型標識", "模型标识", "モデル名", "Upstream model"), m.Model, v => m.Model = v);
        RowButton(AsrProtocolButton(m.Protocol), () => Navigate("asr-protocol"));
        InputRow(m.Protocol == "dashscope-realtime" ? "WebSocket URL" : T("完整接口覆蓋（可選）", "完整接口覆盖（可选）", "API URL上書き（任意）", "Full endpoint override (optional)"), m.Endpoint ?? "", v => m.Endpoint = Blank(v));
        RowLabel(T("即時失敗只使用目前模型頁選好的 HTTP 回退。", "实时失败只使用当前模型页选好的 HTTP 回退。", "リアルタイム失敗時は使用モデルで選んだ HTTP のみ使います。", "A realtime failure uses only the HTTP fallback chosen on Active models."));
        RowButtons(T("返回", "返回", "戻る", "Back"), () => Navigate("asrs"), T("刪除模型", "删除模型", "モデルを削除", "Delete model"), () => { A.Models.Remove(_modelId); Navigate("asrs"); });
    }
    private static void AsrProtocolPicker()
    {
        var current = A.Models[_modelId].Protocol;
        RowButton(T("返回", "返回", "戻る", "Back"), () => Navigate("asr-model"));
        foreach (var protocol in AiConfigurationStore.AsrProtocols)
        {
            var chosen = protocol == current;
            var label = AsrProtocolTitle(protocol) + (chosen ? T("（目前）", "（当前）", "（選択中）", " (selected)") : "");
            var value = protocol;
            RowButton(label, () =>
            {
                var model = A.Models[_modelId];
                if (model.Protocol != value) { model.Protocol = value; model.Endpoint = null; }
                Navigate("asr-model");
            });
        }
    }
    private static string AsrProtocolLabel(string protocol) => protocol switch
    {
        "gemini-audio" => "Gemini", "qwen-http" => "Qwen", "dashscope-realtime" => "Realtime", "audio-transcriptions" => "OpenAI", _ => protocol
    };
    private static string AsrProtocolTitle(string protocol) => protocol switch
    {
        "gemini-audio" => "Gemini", "qwen-http" => "Qwen HTTP", "dashscope-realtime" => "DashScope Realtime", "audio-transcriptions" => "OpenAI Audio", _ => protocol
    };
    private static string AsrProtocolButton(string protocol)
    {
        var shown = string.IsNullOrWhiteSpace(protocol) ? T("未選擇", "未选择", "未選択", "Not selected") : AsrProtocolTitle(protocol);
        return T("API 協定：", "API 协议：", "API プロトコル：", "API protocol: ") + shown;
    }
    private static void Pick(bool chat, bool none, Action<AiModelReference?> selected)
    { _pickChat = chat; _pickNone = none; _pick = selected; Navigate("picker"); }
    private static void Picker()
    {
        var entries = new List<(AiModelReference? Ref, string Name)>();
        if (_pickNone) entries.Add((null, T("不使用回退", "不使用回退", "フォールバックなし", "No fallback")));
        if (_pickChat)
            foreach (var (id, p) in C.Providers)
                foreach (var (mid, m) in p.ChatModels) entries.Add((new() { ProviderId = id, ModelId = mid }, p.Name + " · " + (m.Name.Length > 0 ? m.Name : m.Model)));
        else
            foreach (var (id, p) in C.AsrProviders)
                foreach (var (mid, m) in p.Models)
                    if (!_pickNone || m.Protocol != "dashscope-realtime") entries.Add((new() { ProviderId = id, ModelId = mid }, p.Name + " · " + (m.Name.Length > 0 ? m.Name : m.Model)));
        RowButton(T("返回", "返回", "戻る", "Back"), () => Navigate("current"));
        foreach (var (r, name) in Slice(entries.ToArray())) RowButton(name, () => { _pick!(r); Navigate("current"); });
        Pagination(entries.Count);
    }
    private static IEnumerable<TItem> Slice<TItem>(TItem[] items)
    { _page = Math.Min(_page, Math.Max(0, (items.Length - 1) / 5)); return items.Skip(_page * 5).Take(5); }
    private static void Pagination(int count)
    {
        var bottom = NextBottom();
        var previous = ButtonAt("◀", () => { _page--; Render(); }, 0f, .25f, bottom, bottom + .075f);
        previous.interactable = _page > 0;
        LabelAt($"{_page + 1} / {Math.Max(1, (count + 4) / 5)}", .28f, .72f, bottom, bottom + .075f);
        var next = ButtonAt("▶", () => { _page++; Render(); }, .75f, 1f, bottom, bottom + .075f);
        next.interactable = (_page + 1) * 5 < count;
    }
    private static float NextBottom() => .81f - _row++ * .085f;
    private static void RowButton(string label, Action action)
    { var y = NextBottom(); ButtonAt(label, action, 0, 1, y, y + .075f); }
    private static void RowButtons(string left, Action a, string right, Action b)
    { var y = NextBottom(); ButtonAt(left, a, 0, .48f, y, y + .075f); ButtonAt(right, b, .52f, 1, y, y + .075f); }
    private static void RowLabel(string label) { var y = NextBottom(); LabelAt(label, 0, 1, y, y + .075f); }
    private static GameObject Clone<TItem>(float left, float right, float bottom, float top) where TItem : Component
    {
        GameObject? prefab = null;
        foreach (var entry in _view!._config._uiTypes)
            if (entry.uiObject != null && entry.uiObject.GetComponent<TItem>() != null) { prefab = entry.uiObject; break; }
        if (prefab == null) throw new InvalidOperationException("The official settings prefab is unavailable: " + typeof(TItem).Name);
        var row = UnityEngine.Object.Instantiate(prefab, _pageRoot!.transform);
        row.name = "LilithAi_" + typeof(TItem).Name;
        Rows.Add(row);
        DetachLocalization(row);
        // Only cloned controls lose layout drivers. Official settings remain untouched.
        foreach (var group in row.GetComponentsInChildren<LayoutGroup>(true)) group.enabled = false;
        foreach (var fitter in row.GetComponentsInChildren<ContentSizeFitter>(true)) fitter.enabled = false;
        foreach (var fitter in row.GetComponentsInChildren<AspectRatioFitter>(true)) fitter.enabled = false;
        ResponsiveChildren(row.GetComponent<RectTransform>());
        row.SetActive(true);
        var layout = row.GetComponent<LayoutElement>() ?? row.AddComponent<LayoutElement>(); layout.ignoreLayout = true;
        var rect = row.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(left, bottom); rect.anchorMax = new Vector2(right, top);
        rect.offsetMin = Vector2.zero; rect.offsetMax = Vector2.zero; rect.localScale = Vector3.one;
        return row;
    }
    private static void DetachLocalization(GameObject clone)
    {
        // These clones display Mod labels. Native localization (including deferred updates)
        // must not restore the prefab's original setting name or append its format arguments.
        foreach (var binding in clone.GetComponentsInChildren<AutoBindLocalizeStringEvent>(true))
        {
            binding.enabled = false;
            UnityEngine.Object.Destroy(binding);
        }
        foreach (var localization in clone.GetComponentsInChildren<LocalizeStringEvent>(true))
        {
            localization.enabled = false;
            localization.OnUpdateString = new UnityEventString();
            UnityEngine.Object.Destroy(localization);
        }
    }
    // Convert the prefab's fixed-size child geometry before resizing its root. This preserves
    // native decorations while making the button's hit target and graphic follow the row bounds.
    private static void ResponsiveChildren(RectTransform parent)
    {
        var size = parent.rect.size;
        for (var i = 0; i < parent.childCount; i++)
        {
            var child = parent.GetChild(i).GetComponent<RectTransform>();
            if (child == null) continue;
            ResponsiveChildren(child);
            if (size.x <= 0 || size.y <= 0) continue;
            var min = child.anchorMin; var max = child.anchorMax;
            var offsetMin = child.offsetMin; var offsetMax = child.offsetMax;
            child.anchorMin = new Vector2(min.x + offsetMin.x / size.x, min.y + offsetMin.y / size.y);
            child.anchorMax = new Vector2(max.x + offsetMax.x / size.x, max.y + offsetMax.y / size.y);
            child.offsetMin = Vector2.zero; child.offsetMax = Vector2.zero;
        }
    }
    private static void Place(Component child, Transform parent, float left = 0, float right = 1, float bottom = 0, float top = 1, float inset = 0)
    {
        var rect = child.GetComponent<RectTransform>();
        if (rect == parent) return;
        rect.SetParent(parent, false); rect.localScale = Vector3.one; rect.localRotation = Quaternion.identity;
        rect.anchorMin = new Vector2(left, bottom); rect.anchorMax = new Vector2(right, top);
        rect.offsetMin = new Vector2(inset, inset); rect.offsetMax = new Vector2(-inset, -inset);
    }
    private static void TextStyle(TMP_Text label, string value)
    {
        label.text = value; label.richText = false; label.enableAutoSizing = true;
        // The official big-button font is larger than a settings row. Use the native sidebar's
        // typographic size, not a fixed size that scales into oversized text on the game canvas.
        var nativeSize = _view!._tabItemPrefab._titleText.fontSize;
        label.fontSizeMax = nativeSize; label.fontSizeMin = nativeSize * .6f;
        label.fontSize = nativeSize; label.overflowMode = TextOverflowModes.Ellipsis;
        label.raycastTarget = false;
    }
    private static Button ButtonAt(string label, Action action, float left, float right, float bottom, float top)
    {
        var row = Clone<SettingBigButtonItem>(left, right, bottom, top);
        var item = row.GetComponent<SettingBigButtonItem>(); item.OnValueChanged = null; item.enabled = false;
        Place(item._button, row.transform);
        if (item._button.targetGraphic != null) Place(item._button.targetGraphic, item._button.transform);
        Place(item._buttonText, item._button.transform, inset: 4f);
        TextStyle(item._buttonText, label); item._button.onClick = new Button.ButtonClickedEvent();
        item._button.onClick.AddListener(DelegateSupport.ConvertDelegate<UnityAction>(action));
        return item._button;
    }
    private static TMP_Text LabelAt(string label, float left, float right, float bottom, float top)
    {
        var button = ButtonAt(label, () => { }, left, right, bottom, top);
        button.interactable = false;
        // Keep the native font/material, but a text-only line is not a fake disabled setting.
        if (button.targetGraphic != null) button.targetGraphic.enabled = false;
        var text = button.GetComponentInChildren<TMP_Text>();
        text.enableWordWrapping = true;
        return text;
    }
    private static void InputRow(string label, string value, Action<string> changed, bool password = false, string placeholder = "")
    {
        var y = NextBottom(); var row = Clone<SettingInputFieldItem>(0, 1, y, y + .075f);
        var item = row.GetComponent<SettingInputFieldItem>(); item.OnValueChanged = null; item.enabled = false;
        Place(item._nameText, row.transform, 0, .29f); TextStyle(item._nameText, label);
        var input = item._inputField;
        Place(input, row.transform, .31f, .91f, .08f, .92f);
        if (input.targetGraphic != null)
        {
            Place(input.targetGraphic, input.transform);
            input.targetGraphic.raycastTarget = true;
        }
        var viewport = input.textViewport != null ? input.textViewport : input.GetComponent<RectTransform>();
        Place(viewport, input.transform, inset: 3f);
        if (input.textComponent != null)
        {
            Place(input.textComponent, viewport);
            TextStyle(input.textComponent, "");
            // Clicks on the glyphs must reach the field, and autosize fights the caret.
            input.textComponent.raycastTarget = true;
            input.textComponent.enableAutoSizing = false;
            input.textComponent.enableWordWrapping = false;
            input.textComponent.overflowMode = TextOverflowModes.Overflow;
            input.textComponent.alignment = TextAlignmentOptions.MidlineLeft;
        }
        if (input.placeholder != null) Place(input.placeholder, viewport);
        Place(item._editButton, row.transform, .93f, 1f, .08f, .92f);
        input.gameObject.SetActive(true); input.interactable = true; input.readOnly = false;
        input.characterLimit = 0;
        input.contentType = password ? TMP_InputField.ContentType.Password : TMP_InputField.ContentType.Standard;
        input.lineType = TMP_InputField.LineType.SingleLine;
        var fieldId = _providerId + "/" + _modelId + "/" + label;
        input.SetTextWithoutNotify(InvalidInputs.TryGetValue(fieldId, out var invalid) ? invalid : value);
        // Replacing the prefab events drops SettingInputFieldItem's select hook, which is what
        // asks the transparent window to deliver keystrokes. Arm that around focus explicitly.
        input.onValueChanged = new TMP_InputField.OnChangeEvent(); input.onEndEdit = new TMP_InputField.SubmitEvent();
        input.onSelect = new TMP_InputField.SelectionEvent(); input.onDeselect = new TMP_InputField.SelectionEvent();
        if (input.placeholder is TMP_Text text) { TextStyle(text, placeholder); text.raycastTarget = false; }
        input.onSelect.AddListener(DelegateSupport.ConvertDelegate<UnityAction<string>>((Action<string>)(_ => ArmKeyboard())));
        input.onDeselect.AddListener(DelegateSupport.ConvertDelegate<UnityAction<string>>((Action<string>)(_ => DisarmKeyboard())));
        input.onEndEdit.AddListener(DelegateSupport.ConvertDelegate<UnityAction<string>>((Action<string>)(_ => DisarmKeyboard())));
        input.onValueChanged.AddListener(DelegateSupport.ConvertDelegate<UnityAction<string>>((Action<string>)(v =>
        {
            try { changed(v); InvalidInputs.Remove(fieldId); _error = InvalidInputs.Count > 0 ? NumericError() : ""; }
            catch (FormatException) { InvalidInputs[fieldId] = v; _error = NumericError(); }
            catch (OverflowException) { InvalidInputs[fieldId] = v; _error = NumericError(); }
            if (_status != null) _status.text = _error;
        })));
        item._editButton.onClick = new Button.ButtonClickedEvent();
        item._editButton.onClick.AddListener(DelegateSupport.ConvertDelegate<UnityAction>((Action)(() => { ArmKeyboard(); input.Select(); input.ActivateInputField(); })));
    }
    // The settings window does not forward keys to Unity until keyboard input is armed.
    private static void ArmKeyboard()
    {
        if (_keyboardArmed) return;
        MemoUiKit.BeginKeyboardInput();
        _keyboardArmed = true;
    }
    private static void DisarmKeyboard()
    {
        if (!_keyboardArmed) return;
        _keyboardArmed = false;
        MemoUiKit.EndKeyboardInput();
    }
    private static void Options(string label, string[] values, string current, Action<string> changed, string[]? labels = null)
    {
        var y = NextBottom(); var row = Clone<SettingToggleItem>(0, 1, y, y + .075f);
        var item = row.GetComponent<SettingToggleItem>(); item.OnValueChanged = null; item.enabled = false;
        Place(item._nameText, row.transform, 0, .29f); TextStyle(item._nameText, label);
        var root = item._toggleRoot;
        Place(root, row.transform, .31f, 1f, .08f, .92f);
        // The official option template is a sibling of ToggleGroup, not an option in it.
        // Its Toggle is a child and its text is a sibling of that Toggle.
        item._togglePrefab.SetActive(false);
        for (var i = root.childCount - 1; i >= 0; i--) { var child = root.GetChild(i).gameObject; child.SetActive(false); UnityEngine.Object.Destroy(child); }
        for (var i = 0; i < values.Length; i++)
        {
            var v = values[i]; var option = UnityEngine.Object.Instantiate(item._togglePrefab, root);
            DetachLocalization(option);
            ResponsiveChildren(option.GetComponent<RectTransform>());
            Place(option.GetComponent<RectTransform>(), root, (float)i / values.Length, (float)(i + 1) / values.Length - .01f);
            var toggle = option.GetComponentInChildren<Toggle>(true);
            Place(toggle, option.transform, 0, .17f, .15f, .85f);
            var text = option.GetComponentInChildren<TMP_Text>(true);
            Place(text, option.transform, .20f, 1f, inset: 2f);
            toggle.onValueChanged = new Toggle.ToggleEvent(); toggle.group = item._toggleGroup;
            toggle.SetIsOnWithoutNotify(v == current);
            TextStyle(text, labels == null ? v : labels[i]);
            toggle.onValueChanged.AddListener(DelegateSupport.ConvertDelegate<UnityAction<bool>>((Action<bool>)(on => { if (on) changed(v); })));
            option.SetActive(true);
        }
    }

    private static void Switch(string label, bool value, Action<bool> changed)
    {
        var y = NextBottom(); var row = Clone<SettingSwitchItems>(0, 1, y, y + .075f);
        var item = row.GetComponent<SettingSwitchItems>(); item.OnValueChanged = null;
        item.InitWithLiteralText(label, value); item.enabled = false;
        Place(item._nameText, row.transform, 0, .29f); TextStyle(item._nameText, label);
        Place(item._onButton, row.transform, .31f, .64f, .08f, .92f);
        Place(item._offButton, row.transform, .66f, 1f, .08f, .92f);
        foreach (var text in row.GetComponentsInChildren<TMP_Text>(true)) TextStyle(text, text.text);
        item._onButton.onClick = new Button.ButtonClickedEvent(); item._offButton.onClick = new Button.ButtonClickedEvent();
        item._onButton.onClick.AddListener(DelegateSupport.ConvertDelegate<UnityAction>((Action)(() => { changed(true); item.ApplyValue(true, false); })));
        item._offButton.onClick.AddListener(DelegateSupport.ConvertDelegate<UnityAction>((Action)(() => { changed(false); item.ApplyValue(false, false); })));
    }
}
