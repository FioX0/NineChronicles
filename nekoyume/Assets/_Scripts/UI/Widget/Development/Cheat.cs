using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Libplanet.Action;
using Libplanet.Action.State;
using Nekoyume.Action;
using Nekoyume.Battle;
using Nekoyume.EnumType;
using Nekoyume.Game.Character;
using Nekoyume.Model.BattleStatus;
using Nekoyume.Model.EnumType;
using Nekoyume.Model.Skill;
using Nekoyume.Model.State;
using Nekoyume.State;
using Nekoyume.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Skill = Nekoyume.Model.Skill.Skill;
using Text = UnityEngine.UI.Text;
using Libplanet.Common;
using NCTx = Libplanet.Types.Tx.Transaction;
using Nekoyume.Action.Loader;
using Nekoyume.Game; // for TableSheets
using Nekoyume.Game.Battle; // for BattleRenderer
using Cysharp.Threading.Tasks; // UniTask
using Nekoyume.Game.Scene; // NcSceneManager, SceneType
using Bencodex.Types;
using UniRx;
using URx = UniRx.ObservableExtensions;
using Nekoyume.Helper;
using Nekoyume.Blockchain;
using Nekoyume.Model;
using Nekoyume.Model.Item;
using Nekoyume.Arena;

namespace Nekoyume
{
    public class Cheat : Widget
    {
        private static Cheat Instance;

        public TextMeshProUGUI Logs;
        public Transform Peers;
        public Transform StagedTxs;
        public Transform Blocks;
        public Button BtnOpen;
        public Button buttonBase;
        public ScrollRect list;
        public ScrollRect skillList;
        public HorizontalLayoutGroup skillPanel;
        public Dropdown TableSheetsDropdown;
        public Transform OnChainTableSheet;
        public Transform LocalTableSheet;
        public GameObject PatchButton;
        public GameObject[] Views;

        private Dictionary<string, string> TableAssets;

        private int _viewIndex;
        private Transform _modal;
        private StringBuilder _logString = new();
        private BattleLog.Result _result;
        private int[,] _stageRange;
        private Skill[] _skills;
        private Skill _selectedSkill;
        public override WidgetType WidgetType => WidgetType.Development;

        public class DebugRandom : IRandom
        {
            public DebugRandom()
            {
            }

            public DebugRandom(int seed)
            {
                _random = new System.Random(seed);
            }

            private readonly System.Random _random = new();

            public int Seed => throw new NotImplementedException();

            public int Next()
            {
                return _random.Next();
            }

            public int Next(int maxValue)
            {
                return _random.Next(maxValue);
            }

            public int Next(int minValue, int maxValue)
            {
                return _random.Next(minValue, maxValue);
            }

            public void NextBytes(byte[] buffer)
            {
                _random.NextBytes(buffer);
            }

            public double NextDouble()
            {
                return _random.NextDouble();
            }
        }

        public static void Display(string target, string text)
        {
            switch (target)
            {
                case "Logs":
                    Instance.Logs.text = text;
                    break;
                case "Peers":
                    Instance.Peers.Find("TextRect/Text").GetComponent<TextMeshProUGUI>().text =
                        text;
                    Instance.Refresh(Instance.Peers);
                    break;
                case "StagedTxs":
                    Instance.StagedTxs.Find("TextRect/Text").GetComponent<TextMeshProUGUI>().text =
                        text;
                    Instance.Refresh(Instance.StagedTxs);
                    break;
                case nameof(Blocks):
                    Instance.Blocks.Find("TextRect/InputField").GetComponent<InputField>().text =
                        text;
                    break;
                case nameof(OnChainTableSheet):
                    Instance.OnChainTableSheet.Find("TextRect/Text").GetComponent<TextMeshProUGUI>()
                        .text = text;
                    Instance.Refresh(Instance.OnChainTableSheet);
                    break;
                case nameof(LocalTableSheet):
                    Instance.LocalTableSheet.Find("TextRect/Text").GetComponent<TextMeshProUGUI>()
                        .text = text;
                    Instance.Refresh(Instance.LocalTableSheet);
                    break;
            }
        }

        public void RefreshTableSheets()
        {
            var tableName = TableSheetsDropdown.options.Count == 0 ? string.Empty : GetTableName();
            var tableCsvAssets = Game.Game.GetTableCsvAssets();
            IImmutableDictionary<string, string> tableSheets =
                GetCurrentTableCSV(tableCsvAssets.Keys.ToList()).ToImmutableDictionary();
            if (tableSheets.TryGetValue(tableName, out var onChainTableCsv))
            {
                // Display(nameof(OnChainTableSheet), onChainTableCsv);
                Display(nameof(OnChainTableSheet), "...");
            }
            else
            {
                Display(nameof(OnChainTableSheet), "No content.");
            }

            if (TableAssets.TryGetValue(tableName, out var localTableCsv))
            {
                // Display(nameof(LocalTableSheet), localTableCsv);
            }
            else
            {
                // Display(nameof(LocalTableSheet), "No content.");
                Display(nameof(LocalTableSheet), "...");
            }

            PatchButton.SetActive(onChainTableCsv != localTableCsv);
        }

        public static void Log(string text)
        {
            Instance._logString.Insert(0, $"> {text}\n");
            Instance.Logs.text += Instance._logString.ToString();
        }

        private void Refresh(Transform target)
        {
            // 마스크에 짤려서 사이즈 조정
            var delta =
                target.Find("TextRect/Text").GetComponent<TextMeshProUGUI>().preferredHeight -
                target.GetComponent<RectTransform>().rect.height;
            target.Find("TextRect/Text").GetComponent<RectTransform>().sizeDelta =
                new Vector2(0, delta < 0 ? 0 : delta);

            // 스크롤바 조정
            if (delta < 0)
            {
                target.Find("Scrollbar").gameObject.SetActive(false);
                ScrollBarHandler(target, 0);
            }
            else
            {
                target.Find("Scrollbar").gameObject.SetActive(true);
                target.Find("Scrollbar").GetComponent<Scrollbar>().size =
                    target.GetComponent<RectTransform>().rect.height /
                    target.Find("TextRect/Text").GetComponent<TextMeshProUGUI>().preferredHeight;
            }
        }

        private void ScrollBarHandler(Transform target, float location)
        {
            var delta =
                target.Find("TextRect/Text").GetComponent<TextMeshProUGUI>().preferredHeight -
                target.GetComponent<RectTransform>().rect.height;
            target.Find("TextRect/Text").GetComponent<RectTransform>().anchoredPosition =
                delta > 0 ? new Vector2(0, delta * (location - 1)) : new Vector2(0, 0);
        }

        public void Patch()
        {
            var tableName = GetTableName();
            if (TableAssets.TryGetValue(tableName, out var localTableCsv))
            {
                URx.Subscribe(Game.Game.instance.ActionManager
                    .PatchTableSheet(tableName, localTableCsv));
            }
        }

        private string GetTableName()
        {
            return TableSheetsDropdown.options[TableSheetsDropdown.value].text;
        }

        protected override void Awake()
        {
            base.Awake();

            Instance = this;
            _modal = transform.Find("Modal");
            _modal.gameObject.SetActive(false);
#if DEBUG
#else
            Transform btn = transform.Find("Btn");
            btn.gameObject.SetActive(false);
#endif

            CloseWidget = null;
        }

#if LIB9C_DEV_EXTENSIONS || UNITY_EDITOR
        protected override void Update()
        {
            UpdateInput();
        }
#endif
        public override void Show(bool ignoreShowAnimation = false)
        {
            _modal.gameObject.SetActive(true);

            Peers.Find("Scrollbar")
                .GetComponent<Scrollbar>()
                .onValueChanged
                .AddListener((location) => ScrollBarHandler(Peers, location));
            StagedTxs.Find("Scrollbar")
                .GetComponent<Scrollbar>()
                .onValueChanged
                .AddListener((location) => ScrollBarHandler(StagedTxs, location));
            Blocks.Find("Scrollbar")
                .GetComponent<Scrollbar>()
                .onValueChanged
                .AddListener((location) => ScrollBarHandler(Blocks, location));
            OnChainTableSheet.Find("Scrollbar")
                .GetComponent<Scrollbar>()
                .onValueChanged
                .AddListener((location) => ScrollBarHandler(OnChainTableSheet, location));
            LocalTableSheet.Find("Scrollbar")
                .GetComponent<Scrollbar>()
                .onValueChanged
                .AddListener((location) => ScrollBarHandler(LocalTableSheet, location));
            Refresh(OnChainTableSheet);
            Refresh(LocalTableSheet);
            Refresh(Peers);
            Refresh(StagedTxs);
            ScrollBarHandler(Peers, 0);
            ScrollBarHandler(StagedTxs, 0);
            ScrollBarHandler(OnChainTableSheet, 0);
            ScrollBarHandler(LocalTableSheet, 0);

            BtnOpen.gameObject.SetActive(false);
            foreach (var i in Enumerable.Range(1,
                         Game.Game.instance.TableSheets.StageWaveSheet.Count))
            {
                var newButton = Instantiate(buttonBase, list.content);
                newButton.GetComponentInChildren<Text>().text = i.ToString();
                newButton.onClick.AddListener(() => DummyBattle(i));
                newButton.gameObject.SetActive(true);
            }

            var skills = new List<Skill>();
            foreach (var skillRow in Game.Game.instance.TableSheets.SkillSheet)
            {
                var skill = SkillFactory.GetV1(skillRow, 50, 100);
                skills.Add(skill);
                var newButton = Instantiate(buttonBase, skillList.content);
                newButton.GetComponentInChildren<Text>().text =
                    $"{skillRow.GetLocalizedName()}_{skillRow.ElementalType}";
                newButton.onClick.AddListener(() => SelectSkill(skill));
                newButton.gameObject.SetActive(true);
            }

            _skills = skills.ToArray();

            TableAssets = GetTableAssetsHavingDifference();
            TableSheetsDropdown.options =
                TableAssets.Keys.Select(s => new Dropdown.OptionData(s)).ToList();
            if (TableSheetsDropdown.options.Count == 0)
            {
                NcDebug.Log("It seems there is no table having difference.");
                Display(nameof(OnChainTableSheet), "No content.");
                Display(nameof(LocalTableSheet), "No content.");
                PatchButton.SetActive(false);
            }
            else
            {
                RefreshTableSheets();
            }

            base.Show(ignoreShowAnimation);
        }

        private static Dictionary<string, string> GetTableAssetsHavingDifference()
        {
            var tableCsvAssets = Game.Game.GetTableCsvAssets();
            var tableCsvNames = tableCsvAssets.Keys.ToList();
            var currentTables = GetCurrentTableCSV(tableCsvNames);
            return tableCsvAssets
                .Where(pair => pair.Value != currentTables[pair.Key])
                .ToDictionary(pair => pair.Key, pair => pair.Value);
        }


        private static Dictionary<string, string> GetCurrentTableCSV(IEnumerable<string> tableNames)
        {
            var result = new Dictionary<string, string>();
            foreach (var name in tableNames)
            {
                var value = string.Empty;
                var state = Game.Game.instance.Agent.GetState(
                    ReservedAddresses.LegacyAccount,
                    Addresses.TableSheet.Derive(name));
                if (!(state is null))
                {
                    value = state.ToDotnetString();
                }

                result[name] = value;
            }

            return result;
        }

        public override void Close(bool ignoreCloseAnimation = false)
        {
            Peers.Find("Scrollbar").GetComponent<Scrollbar>().onValueChanged.RemoveAllListeners();
            StagedTxs.Find("Scrollbar").GetComponent<Scrollbar>().onValueChanged
                .RemoveAllListeners();
            foreach (Transform child in list.content.transform)
            {
                Destroy(child.gameObject);
            }

            list.gameObject.SetActive(false);
            skillPanel.gameObject.SetActive(false);

            _modal.gameObject.SetActive(false);
            BtnOpen.gameObject.SetActive(true);
        }

        public override bool IsActive()
        {
            return _modal.gameObject.activeSelf;
        }

        public void SwitchView()
        {
            Views[_viewIndex].SetActive(false);
            _viewIndex = (_viewIndex + 1) % Views.Length;
            Views[_viewIndex].SetActive(true);
        }

        public void HandleClick(GameObject sender)
        {
#if DEBUG
            Invoke(sender.name, 0.0f);
#endif
        }

        private void LevelUp()
        {
            var enemyObj = GameObject.Find("Enemy");
            if (enemyObj == null)
            {
                Log("Need Enemy.");
                return;
            }

            var playerObj = GameObject.Find("Player");
            if (playerObj != null)
            {
                var player = playerObj.GetComponent<Nekoyume.Game.Character.Player>();
                player.Level += 1;
                Log($"Level Up to {player.Level}");
            }

            var enemy = enemyObj.GetComponent<StageMonster>();
            Game.Event.OnEnemyDeadStart.Invoke(enemy);
        }

        private void SpeedUp()
        {
            Time.timeScale = 2.0f;
            Log($"Speed Up to {Time.timeScale}");
        }

        private void DummyBattleWin()
        {
            _result = BattleLog.Result.Win;
            list.gameObject.SetActive(true);
        }

        private void DummyBattleLose()
        {
            _result = BattleLog.Result.Lose;
            list.gameObject.SetActive(true);
        }

        private void DummyBattle(int stageId)
        {
            Find<BattleResultPopup>()?.Close();
            Find<LobbyMenu>()?.Close();
            Find<LobbyMenu>()?.ShowWorld();

            if (!Game.Game.instance.TableSheets.WorldSheet.TryGetByStageId(stageId,
                    out var worldRow))
            {
                throw new KeyNotFoundException(
                    $"WorldSheet.TryGetByStageId() {nameof(stageId)}({stageId})");
            }

            var tableSheets = Game.Game.instance.TableSheets;
            var random = new DebugRandom();
            var avatarState = States.Instance.CurrentAvatarState;
            var simulator = new StageSimulator(
                random,
                avatarState,
                new List<Guid>(),
                States.Instance.AllRuneState,
                States.Instance.CurrentRuneSlotStates[BattleType.Adventure],
                new List<Skill>(),
                worldRow.Id,
                stageId,
                tableSheets.StageSheet[stageId],
                tableSheets.StageWaveSheet[stageId],
                avatarState.worldInformation.IsStageCleared(stageId),
                StageRewardExpHelper.GetExp(avatarState.level, stageId),
                tableSheets.GetStageSimulatorSheets(),
                tableSheets.EnemySkillSheet,
                tableSheets.CostumeStatSheet,
                StageSimulator.GetWaveRewards(
                    random,
                    tableSheets.StageSheet[stageId],
                    tableSheets.MaterialItemSheet),
                States.Instance.CollectionState.GetEffects(tableSheets.CollectionSheet),
                tableSheets.BuffLimitSheet,
                tableSheets.BuffLinkSheet,
                true,
                States.Instance.GameConfigState.ShatterStrikeMaxDamage
            );
            simulator.Simulate();
            simulator.Log.result = _result;

            var stage = Game.Game.instance.Stage;
            stage.PlayStage(simulator.Log);

            Close();
        }

        private void DummySkill()
        {
            skillPanel.gameObject.SetActive(true);
        }

        private void DeletePlayerPrefs()
        {
            PlayerPrefs.DeleteAll();
        }

        private void SelectSkill(Skill skill)
        {
            _selectedSkill = skill;
            DummyBattle(1);
        }

        private void UpdateInput()
        {
            if (Input.GetKeyDown(KeyCode.Alpha1))
            {
                Time.timeScale = 1;
            }

            if (Input.GetKeyDown(KeyCode.Alpha2))
            {
                Time.timeScale = 2;
            }

            if (Input.GetKeyDown(KeyCode.Alpha3))
            {
                Time.timeScale = 3;
            }

            if (Input.GetKeyDown(KeyCode.Alpha4))
            {
                Time.timeScale = 4;
            }

            // Enter to paste Base64 transaction and replay (choose action)
            if (Input.GetKeyDown(KeyCode.Return))
            {
                OpenTxReplayInput();
            }
        }

        private const string ReplayPrevStateHex =
            "16baab0d1e380a0917787f40fc0e66d4d0241bbc1a924386b2fc306b16f6f1bd";

        private const string ReplayNewStateHex =
            "724d758e28ef5dc83eafa6dba78f9100c85e6e122f1843277c53edf941aa0b13";

        private const long ReplayBlockIndex = 6972101L;

        private void TryOpenTxReplayInput()
        {
            var popup = Widget.Find<InputBoxPopup>();
            if (popup == null)
            {
                NcDebug.LogWarning("InputBoxPopup not found.");
                return;
            }

            if (popup.IsActive())
            {
                return;
            }

            popup.CloseCallback = result =>
            {
                if (result != ConfirmResult.Yes)
                {
                    return;
                }

                var text = popup.text?.Trim();
                // If input likely sanitized (no '+' or '/' but clipboard has them), prefer clipboard
                var cb = GUIUtility.systemCopyBuffer;
                if (!string.IsNullOrEmpty(cb) && (text?.IndexOfAny(new[] { '+', '/' }) ?? -1) < 0 &&
                    (cb.IndexOfAny(new[] { '+', '/' }) >= 0))
                {
                    text = cb;
                }

                if (string.IsNullOrEmpty(text))
                {
                    NcDebug.LogWarning("Empty Base64 input.");
                    return;
                }

                AutoReplayFromBase64(text);
            };

            popup.Show(
                "Enter Base64 transaction",
                "Paste Libplanet Transaction (Base64)",
                localize: false);
        }

        private void TryOpenArenaReplayInput()
        {
            var popup = Widget.Find<InputBoxPopup>();
            if (popup == null)
            {
                NcDebug.LogWarning("InputBoxPopup not found.");
                return;
            }

            if (popup.IsActive())
            {
                return;
            }

            popup.CloseCallback = result =>
            {
                if (result != ConfirmResult.Yes)
                {
                    return;
                }

                var text = popup.text?.Trim();
                var cb = GUIUtility.systemCopyBuffer;
                if (!string.IsNullOrEmpty(cb) && (text?.IndexOfAny(new[] { '+', '/' }) ?? -1) < 0 &&
                    (cb.IndexOfAny(new[] { '+', '/' }) >= 0))
                {
                    text = cb;
                }

                if (string.IsNullOrEmpty(text))
                {
                    NcDebug.LogWarning("Empty Base64 input.");
                    return;
                }

                ReplayArenaBattleFromBase64(text);
            };

            popup.Show(
                "Enter Base64 transaction",
                "Paste Arena Battle Libplanet Transaction (Base64)",
                localize: false);
        }

        public static void OpenArenaReplayInput()
        {
            UniTask.Void(async () =>
            {
                if (NcSceneManager.Instance.ESceneType != SceneType.Game)
                {
                    await NcSceneManager.Instance.LoadScene(SceneType.Game);
                    await UniTask.NextFrame();
                }

                Instance?.TryOpenArenaReplayInput();
            });

            if (Instance != null)
            {
                Instance.TryOpenArenaReplayInput();
                return;
            }

            var popup = Widget.Find<InputBoxPopup>();
            if (popup == null || popup.IsActive())
            {
                return;
            }

            popup.CloseCallback = result =>
            {
                if (result != ConfirmResult.Yes)
                {
                    return;
                }

                var text = popup.text?.Trim();
                var cb = GUIUtility.systemCopyBuffer;
                if (!string.IsNullOrEmpty(cb) && (text?.IndexOfAny(new[] { '+', '/' }) ?? -1) < 0 &&
                    (cb.IndexOfAny(new[] { '+', '/' }) >= 0))
                {
                    text = cb;
                }

                if (string.IsNullOrEmpty(text))
                {
                    NcDebug.LogWarning("Empty Base64 input.");
                    return;
                }

                ReplayArenaBattleFromBase64(text);
            };

            popup.Show(
                "Enter Base64 transaction",
                "Paste Arena Battle Libplanet Transaction (Base64)",
                localize: false);
        }

        public static void OpenTxReplayInput()
        {
            UniTask.Void(async () =>
            {
                if (NcSceneManager.Instance.ESceneType != SceneType.Game)
                {
                    await NcSceneManager.Instance.LoadScene(SceneType.Game);
                    await UniTask.NextFrame();
                }

                Instance?.TryOpenTxReplayInput();
            });

            if (Instance != null)
            {
                Instance.TryOpenTxReplayInput();
                return;
            }

            // Fallback: open popup even when Cheat widget is not instantiated
            var popup = Widget.Find<InputBoxPopup>();
            if (popup == null || popup.IsActive())
            {
                return;
            }

            popup.CloseCallback = result =>
            {
                if (result != ConfirmResult.Yes)
                {
                    return;
                }

                var text = popup.text?.Trim();
                // If input likely sanitized (no '+' or '/' but clipboard has them), prefer clipboard
                var cb = GUIUtility.systemCopyBuffer;
                if (!string.IsNullOrEmpty(cb) && (text?.IndexOfAny(new[] { '+', '/' }) ?? -1) < 0 &&
                    (cb.IndexOfAny(new[] { '+', '/' }) >= 0))
                {
                    text = cb;
                }

                if (string.IsNullOrEmpty(text))
                {
                    NcDebug.LogWarning("Empty Base64 input.");
                    return;
                }

                AutoReplayFromBase64(text);
            };

            popup.Show(
                "Enter Base64 transaction",
                "Paste Libplanet Transaction (Base64)",
                localize: false);
        }

        private static void ReplayHackAndSlashFromBase64(string base64)
        {
            byte[] raw;
            try
            {
                // Debug: log raw input info
                NcDebug.Log(
                    $"[TxReplay] Raw length={base64?.Length ?? 0}, head='{Preview(base64, 120)}', tail='{PreviewTail(base64, 120)}'");

                var normalized = NormalizeBase64(base64);
                var invalidIdx = FirstInvalidBase64Index(normalized);
                NcDebug.Log(
                    $"[TxReplay] Normalized length={normalized.Length}, mod4={normalized.Length % 4}, invalidIdx={(invalidIdx >= 0 ? invalidIdx.ToString() : "none")}");
                if (invalidIdx >= 0)
                {
                    var ch = normalized[invalidIdx];
                    NcDebug.LogError(
                        $"[TxReplay] Invalid char '{ch}' (U+{((int)ch):X4}) at index {invalidIdx}");
                }

                raw = Convert.FromBase64String(normalized);
            }
            catch (Exception e)
            {
                // Fallback: try clipboard directly
                var cb = GUIUtility.systemCopyBuffer;
                NcDebug.LogError($"Invalid Base64 (typed). Trying clipboard... {e}");
                try
                {
                    var normalizedCb = NormalizeBase64(cb);
                    raw = Convert.FromBase64String(normalizedCb);
                }
                catch (Exception e2)
                {
                    NcDebug.LogError($"Invalid Base64 (clipboard): {e2}");
                    return;
                }
            }

            NCTx tx;
            try
            {
                tx = NCTx.Deserialize(raw);
            }
            catch (Exception e)
            {
                NcDebug.LogError($"Failed to deserialize Transaction: {e}");
                return;
            }

            var loader = new NCActionLoader();
            HackAndSlash has = null;
            int idx = 0;
            foreach (var actionValue in tx.Actions)
            {
                try
                {
                    // Read type_id from dictionary form
                    if (actionValue is Bencodex.Types.Dictionary dict &&
                        dict.TryGetValue((Bencodex.Types.Text)"type_id", out var typeVal) &&
                        typeVal is Bencodex.Types.Text typeText)
                    {
                        var typeId = typeText.Value;
                        NcDebug.Log($"[TxReplay] Action[{idx}] type='{typeId}'");

                        if (typeId.StartsWith("hack_and_slash"))
                        {
                            try
                            {
                                Bencodex.Types.IValue plain;
                                if (!dict.TryGetValue((Bencodex.Types.Text)"values",
                                        out var plainObj))
                                {
                                    // Build inner values dict excluding type_id and id
                                    var innerKvs =
                                        new System.Collections.Generic.Dictionary<
                                            Bencodex.Types.IKey, Bencodex.Types.IValue>();
                                    Bencodex.Types.IValue actionIdVal = null;
                                    foreach (var kv in dict)
                                    {
                                        if (kv.Key is Bencodex.Types.Text t)
                                        {
                                            if (t.Value == "type_id") continue;
                                            if (t.Value == "id")
                                            {
                                                actionIdVal = kv.Value;
                                                continue;
                                            }
                                        }

                                        innerKvs[kv.Key] = kv.Value;
                                    }

                                    var innerValues = new Bencodex.Types.Dictionary(innerKvs);

                                    // Wrap as { id?: <id>, values: <innerValues> }
                                    var wrapperKvs =
                                        new System.Collections.Generic.Dictionary<
                                            Bencodex.Types.IKey, Bencodex.Types.IValue>();
                                    if (actionIdVal != null)
                                    {
                                        wrapperKvs[(Bencodex.Types.Text)"id"] = actionIdVal;
                                    }

                                    wrapperKvs[(Bencodex.Types.Text)"values"] = innerValues;
                                    plain = new Bencodex.Types.Dictionary(wrapperKvs);
                                }
                                else
                                {
                                    // Ensure top-level wrapper with values exists
                                    var wrapperKvs =
                                        new System.Collections.Generic.Dictionary<
                                            Bencodex.Types.IKey, Bencodex.Types.IValue>
                                        {
                                            [(Bencodex.Types.Text)"values"] =
                                                (Bencodex.Types.IValue)plainObj,
                                        };
                                    plain = new Bencodex.Types.Dictionary(wrapperKvs);
                                }

                                var manual = new Nekoyume.Action.HackAndSlash();
                                manual.LoadPlainValue(plain);
                                has = manual;
                                NcDebug.Log("[TxReplay] Loaded HackAndSlash via manual fallback.");
                            }
                            catch (System.Exception ex)
                            {
                                NcDebug.LogError($"[TxReplay] Manual fallback failed: {ex}");
                            }

                            // Skip loader regardless to avoid dict 'values' expectation
                            if (has != null)
                            {
                                break;
                            }

                            idx++;
                            continue;
                        }
                    }

                    // Optional: keep loader path too (only if not already resolved)
                    if (has is null)
                    {
                        var loaded = (ActionBase)loader.LoadAction(ReplayBlockIndex, actionValue);
                        if (loaded is HackAndSlash h)
                        {
                            has = h;
                            break;
                        }
                    }
                }
                catch (Exception e)
                {
                    NcDebug.LogWarning($"Action[{idx}] load failed: {e.Message}");
                }

                idx++;
            }

            if (has == null)
            {
                NcDebug.LogWarning("Transaction does not contain HackAndSlash action.");
                return;
            }

            HashDigest<System.Security.Cryptography.SHA256> prev = default;
            HashDigest<System.Security.Cryptography.SHA256> next = default;
            try
            {
                prev = new HashDigest<System.Security.Cryptography.SHA256>(
                    ByteUtil.ParseHex(ReplayPrevStateHex));
                next = new HashDigest<System.Security.Cryptography.SHA256>(
                    ByteUtil.ParseHex(ReplayNewStateHex));
            }
            catch
            {
                // ignore
            }

            var randomSeed = 1267466043;
            var eval = new Lib9c.Renderers.ActionEvaluation<HackAndSlash>
            {
                Action = has,
                Signer = tx.Signer,
                BlockIndex = ReplayBlockIndex,
                TxId = tx.Id,
                OutputState = next,
                PreviousState = prev,
                RandomSeed = randomSeed,
                Extra = new Dictionary<string, Bencodex.Types.IValue>(),
            };

            try
            {
                var tableSheets = TableSheets.Instance;
                var tempPlayer = (AvatarState)States.Instance.CurrentAvatarState.Clone();
                tempPlayer.EquipEquipments(States.Instance
                    .CurrentItemSlotStates[BattleType.Adventure].Equipments);
                var model = eval.GetHackAndSlashReward(
                    tempPlayer,
                    States.Instance.AllRuneState,
                    States.Instance.CurrentRuneSlotStates[BattleType.Adventure],
                    States.Instance.CollectionState,
                    new List<Nekoyume.Model.Skill.Skill>(),
                    tableSheets,
                    out var simulator,
                    out _);

                var log = simulator.Log;
                var stage = Game.Game.instance.Stage;
                stage.StageType = StageType.HackAndSlash;
                stage.PlayCount = eval.Action.TotalPlayCount;
                // Close lobby-related widgets to let stage take over (same as DummyBattle)
                Find<BattleResultPopup>()?.Close();
                Find<LobbyMenu>()?.Close();
                Find<Status>()?.Close();
                // Prepare and play (mirror DummyBattle path)
                UniTask.Void(async () =>
                {
                    try
                    {
                        // Ensure character prefabs are loaded so enemy Spine prefabs like "210004" resolve
                        await Nekoyume.Game.Character.CharacterManager.Instance
                            .LoadCharacterAssetAsync();
                        await ResourceManager.Instance.LoadAllAsync<GameObject>(
                            ResourceManager.CharacterLabel, true);

                        // If this replay is a loss, auto-exit to main at end
                        if (log.result == BattleLog.Result.Lose)
                        {
                            stage.IsExitReserved = true;
                        }

                        // Unblock Stage.CoStageEnd wait (we don’t update avatar state during replays)
                        URx.Subscribe(UniRx.Observable.Take(stage.OnEnterToStageEnd, 1),
                            _ => { stage.IsAvatarStateUpdatedAfterBattle = true; });

                        BattleRenderer.Instance.PrepareStage(log);
                        stage.PlayStage(log);
                    }
                    catch (Exception ex)
                    {
                        NcDebug.LogException(ex);
                    }
                });
            }
            catch (Exception e)
            {
                NcDebug.LogException(e);
            }
        }

        private static void AutoReplayFromBase64(string base64)
        {
            byte[] raw;
            try
            {
                var normalized = NormalizeBase64(base64);
                raw = Convert.FromBase64String(normalized);
            }
            catch
            {
                var cb = GUIUtility.systemCopyBuffer;
                try
                {
                    raw = Convert.FromBase64String(NormalizeBase64(cb));
                }
                catch (Exception e)
                {
                    NcDebug.LogError($"Invalid Base64 for auto-detect: {e}");
                    return;
                }
            }

            NCTx tx;
            try
            {
                tx = NCTx.Deserialize(raw);
            }
            catch (Exception e)
            {
                NcDebug.LogError($"Failed to deserialize Transaction for auto-detect: {e}");
                return;
            }

            // Detect action type-id from first matching action dictionary
            try
            {
                foreach (var actionValue in tx.Actions)
                {
                    if (actionValue is Bencodex.Types.Dictionary dict &&
                        dict.TryGetValue((Bencodex.Types.Text)"type_id", out var typeVal) &&
                        typeVal is Bencodex.Types.Text typeText)
                    {
                        var typeId = typeText.Value;
                        if (typeId.StartsWith("hack_and_slash"))
                        {
                            ReplayHackAndSlashFromBase64(base64);
                            return;
                        }

                        if (typeId.StartsWith("battle"))
                        {
                            ReplayArenaBattleFromBase64(base64);
                            return;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                NcDebug.LogWarning($"Auto-detect failed: {e.Message}");
            }

            NcDebug.LogWarning(
                "Could not detect action type in transaction. Defaulting to HackAndSlash.");
            ReplayHackAndSlashFromBase64(base64);
        }

        private static void ReplayArenaBattleFromBase64(string base64)
        {
            byte[] raw;
            try
            {
                NcDebug.Log(
                    $"[ArenaReplay] Raw length={base64?.Length ?? 0}, head='{Preview(base64, 120)}', tail='{PreviewTail(base64, 120)}'");
                var normalized = NormalizeBase64(base64);
                var invalidIdx = FirstInvalidBase64Index(normalized);
                NcDebug.Log(
                    $"[ArenaReplay] Normalized length={normalized.Length}, mod4={normalized.Length % 4}, invalidIdx={(invalidIdx >= 0 ? invalidIdx.ToString() : "none")}");
                if (invalidIdx >= 0)
                {
                    var ch = normalized[invalidIdx];
                    NcDebug.LogError(
                        $"[ArenaReplay] Invalid char '{ch}' (U+{((int)ch):X4}) at index {invalidIdx}");
                }

                raw = Convert.FromBase64String(normalized);
            }
            catch (Exception e)
            {
                var cb = GUIUtility.systemCopyBuffer;
                NcDebug.LogError($"Invalid Base64 (typed). Trying clipboard... {e}");
                try
                {
                    var normalizedCb = NormalizeBase64(cb);
                    raw = Convert.FromBase64String(normalizedCb);
                }
                catch (Exception e2)
                {
                    NcDebug.LogError($"Invalid Base64 (clipboard): {e2}");
                    return;
                }
            }

            NCTx tx;
            try
            {
                tx = NCTx.Deserialize(raw);
            }
            catch (Exception e)
            {
                NcDebug.LogError($"Failed to deserialize Transaction: {e}");
                return;
            }

            var loader = new NCActionLoader();
            Nekoyume.Action.Arena.Battle battle = null;
            int idx = 0;
            foreach (var actionValue in tx.Actions)
            {
                try
                {
                    if (actionValue is Bencodex.Types.Dictionary dict &&
                        dict.TryGetValue((Bencodex.Types.Text)"type_id", out var typeVal) &&
                        typeVal is Bencodex.Types.Text typeText)
                    {
                        var typeId = typeText.Value;
                        NcDebug.Log($"[ArenaReplay] Action[{idx}] type='{typeId}'");
                        if (typeId.StartsWith("battle"))
                        {
                            try
                            {
                                Bencodex.Types.IValue plain;
                                if (!dict.TryGetValue((Bencodex.Types.Text)"values",
                                        out var plainObj))
                                {
                                    var innerKvs =
                                        new System.Collections.Generic.Dictionary<
                                            Bencodex.Types.IKey, Bencodex.Types.IValue>();
                                    Bencodex.Types.IValue actionIdVal = null;
                                    foreach (var kv in dict)
                                    {
                                        if (kv.Key is Bencodex.Types.Text t)
                                        {
                                            if (t.Value == "type_id") continue;
                                            if (t.Value == "id")
                                            {
                                                actionIdVal = kv.Value;
                                                continue;
                                            }
                                        }

                                        innerKvs[kv.Key] = kv.Value;
                                    }

                                    var innerValues = new Bencodex.Types.Dictionary(innerKvs);
                                    var wrapperKvs =
                                        new System.Collections.Generic.Dictionary<
                                            Bencodex.Types.IKey, Bencodex.Types.IValue>();
                                    if (actionIdVal != null)
                                    {
                                        wrapperKvs[(Bencodex.Types.Text)"id"] = actionIdVal;
                                    }

                                    wrapperKvs[(Bencodex.Types.Text)"values"] = innerValues;
                                    plain = new Bencodex.Types.Dictionary(wrapperKvs);
                                }
                                else
                                {
                                    var wrapperKvs =
                                        new System.Collections.Generic.Dictionary<
                                            Bencodex.Types.IKey, Bencodex.Types.IValue>
                                        {
                                            [(Bencodex.Types.Text)"values"] =
                                                (Bencodex.Types.IValue)plainObj,
                                        };
                                    plain = new Bencodex.Types.Dictionary(wrapperKvs);
                                }

                                var manual = new Nekoyume.Action.Arena.Battle();
                                manual.LoadPlainValue(plain);
                                // Make sure renderer treats this as the current avatar's action
                                manual.myAvatarAddress = States.Instance.CurrentAvatarState.address;
                                battle = manual;
                                NcDebug.Log("[ArenaReplay] Loaded Battle via manual fallback.");
                            }
                            catch (System.Exception ex)
                            {
                                NcDebug.LogError($"[ArenaReplay] Manual fallback failed: {ex}");
                            }

                            if (battle != null)
                            {
                                break;
                            }

                            idx++;
                            continue;
                        }
                    }

                    if (battle is null)
                    {
                        var loaded = (ActionBase)loader.LoadAction(ReplayBlockIndex, actionValue);
                        if (loaded is Nekoyume.Action.Arena.Battle b)
                        {
                            // Make sure renderer treats this as the current avatar's action
                            b.myAvatarAddress = States.Instance.CurrentAvatarState.address;
                            battle = b;
                            break;
                        }
                    }
                }
                catch (Exception e)
                {
                    NcDebug.LogWarning($"Arena Action[{idx}] load failed: {e.Message}");
                }

                idx++;
            }

            if (battle == null)
            {
                NcDebug.LogWarning("Transaction does not contain Arena Battle action.");
                return;
            }

            HashDigest<System.Security.Cryptography.SHA256> prev = default;
            HashDigest<System.Security.Cryptography.SHA256> next = default;
            try
            {
                prev = new HashDigest<System.Security.Cryptography.SHA256>(
                    ByteUtil.ParseHex(ReplayPrevStateHex));
                next = new HashDigest<System.Security.Cryptography.SHA256>(
                    ByteUtil.ParseHex(ReplayNewStateHex));
            }
            catch
            {
            }

            var randomSeed = 931630225;
            var eval = new Lib9c.Renderers.ActionEvaluation<Nekoyume.Action.Arena.Battle>
            {
                Action = battle,
                Signer = tx.Signer,
                BlockIndex = ReplayBlockIndex,
                TxId = tx.Id,
                OutputState = next,
                PreviousState = prev,
                RandomSeed = randomSeed,
                Extra = new Dictionary<string, Bencodex.Types.IValue>(),
            };

            // Mirror ActionRenderHandler's Arena flow to avoid last-action gate
            UniTask.Void(async () =>
            {
                try
                {
                    NcDebug.Log("[ArenaReplay] Start");
                    NcDebug.Log(
                        $"[ArenaReplay] Current scene={NcSceneManager.Instance.ESceneType}");
                    if (NcSceneManager.Instance.ESceneType != SceneType.Game)
                    {
                        NcDebug.Log("[ArenaReplay] Loading Game scene...");
                        await NcSceneManager.Instance.LoadScene(SceneType.Game);
                        await UniTask.NextFrame();
                        NcDebug.Log("[ArenaReplay] Game scene loaded");
                    }

                    // Build digests (align with ActionRenderHandler.GetArenaPlayerDigest)
                    var prevStates = eval.PreviousState;
                    var outputStates = eval.OutputState;
                    var myAddr = eval.Action.myAvatarAddress;
                    var enemyAddr = eval.Action.enemyAvatarAddress;
                    NcDebug.Log(
                        $"[ArenaReplay] Addrs my={myAddr}, enemy={enemyAddr}, blockIndex={eval.BlockIndex}");

                    // Fetch any async states before switching threads
                    NcDebug.Log("[ArenaReplay] Fetching enemy avatar state...");
                    var enemyMap =
                        await Game.Game.instance.Agent.GetAvatarStatesAsync(prevStates,
                            new[] { enemyAddr });
                    var enemyAvatarState = enemyMap[enemyAddr];
                    NcDebug.Log("[ArenaReplay] Enemy avatar state fetched");

                    // Heavy work on thread pool
                    NcDebug.Log("[ArenaReplay] Starting background simulation...");
                    var result = await UniTask.RunOnThreadPool(() =>
                    {
                        var myAvatarState = States.Instance.CurrentAvatarState;

                        var myItemSlotStateAddress =
                            ItemSlotState.DeriveAddress(myAddr, BattleType.Arena);
                        var hasMyItem = StateGetter.TryGetState(
                            outputStates,
                            ReservedAddresses.LegacyAccount,
                            myItemSlotStateAddress,
                            out var rawItemSlotState);
                        var myItemSlotState = hasMyItem
                            ? new ItemSlotState((Bencodex.Types.List)rawItemSlotState)
                            : new ItemSlotState(BattleType.Arena);

                        var myAllRuneState = States.Instance.AllRuneState;
                        var myRuneSlotState =
                            States.Instance.CurrentRuneSlotStates[BattleType.Arena];

                        var myDigest = new ArenaPlayerDigest(
                            myAvatarState,
                            myItemSlotState.Equipments,
                            myItemSlotState.Costumes,
                            myAllRuneState,
                            myRuneSlotState);

                        var enemyItemSlotStateAddress =
                            ItemSlotState.DeriveAddress(enemyAddr, BattleType.Arena);
                        var enemyItemSlotState = StateGetter.GetState(
                            prevStates,
                            ReservedAddresses.LegacyAccount,
                            enemyItemSlotStateAddress) is Bencodex.Types.List enemyRawItemSlotState
                            ? new ItemSlotState(enemyRawItemSlotState)
                            : new ItemSlotState(BattleType.Arena);

                        var enemyAllRuneState =
                            GetStateExtensions.GetAllRuneState(prevStates, enemyAddr);

                        var enemyRuneSlotStateAddress =
                            RuneSlotState.DeriveAddress(enemyAddr, BattleType.Arena);
                        var enemyRuneSlotState = StateGetter.GetState(
                            prevStates,
                            ReservedAddresses.LegacyAccount,
                            enemyRuneSlotStateAddress) is Bencodex.Types.List enemyRawRuneSlotState
                            ? new RuneSlotState(enemyRawRuneSlotState)
                            : new RuneSlotState(BattleType.Arena);

                        var enemyDigest = new ArenaPlayerDigest(
                            enemyAvatarState,
                            enemyItemSlotState.Equipments,
                            enemyItemSlotState.Costumes,
                            enemyAllRuneState,
                            enemyRuneSlotState);

                        var tableSheets = TableSheets.Instance;
                        var arenaSheets = tableSheets.GetArenaSimulatorSheets();
                        var random =
                            new Nekoyume.Blockchain.ActionRenderHandler.LocalRandom(
                                eval.RandomSeed);

                        var simulator = new ArenaSimulator(
                            random,
                            Nekoyume.Action.Arena.Battle.HpIncreasingModifier,
                            States.Instance.GameConfigState.ShatterStrikeMaxDamage);

                        var log = simulator.Simulate(
                            myDigest,
                            enemyDigest,
                            arenaSheets,
                            StateGetter.GetCollectionState(outputStates, myAddr)
                                .GetEffects(tableSheets.CollectionSheet),
                            StateGetter.GetCollectionState(outputStates, enemyAddr)
                                .GetEffects(tableSheets.CollectionSheet),
                            tableSheets.BuffLimitSheet,
                            tableSheets.BuffLinkSheet,
                            true);

                        return (myDigest, enemyDigest, log);
                    });
                    NcDebug.Log($"[ArenaReplay] Simulation finished. Result={result.log.Result}");

                    // Back to main thread for UI
                    await UniTask.SwitchToMainThread();

                    // Close lobby-related widgets to avoid lobby background overlay
                    Find<BattleResultPopup>()?.Close();
                    Find<LobbyMenu>()?.Close();
                    Find<Status>()?.Close();
                    NcDebug.Log("[ArenaReplay] Closed lobby widgets");

                    var arenaPrep = Widget.Find<ArenaBattlePreparation>();
                    if (arenaPrep != null && !arenaPrep.IsActive())
                    {
                        NcDebug.Log("[ArenaReplay] Showing ArenaBattlePreparation UI...");
                        arenaPrep.Show();
                    }

                    arenaPrep?.OnRenderBattleArena(eval);
                    NcDebug.Log("[ArenaReplay] ArenaBattlePreparation rendered");

                    NcDebug.Log("[ArenaReplay] Entering Arena...");
                    Game.Game.instance.Arena.SkipServerPolling = true;
                    Game.Game.instance.Arena.Enter(
                        result.log,
                        new System.Collections.Generic.List<ItemBase>(),
                        result.myDigest,
                        result.enemyDigest,
                        myAddr,
                        enemyAddr,
                        null);
                    NcDebug.Log("[ArenaReplay] Arena.Enter invoked");
                }
                catch (Exception e)
                {
                    NcDebug.LogException(e);
                }
            });
            return;
        }

        private static string NormalizeBase64(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return string.Empty;
            }

            // Trim and remove whitespace/newlines
            var s = input.Trim()
                .Replace("\n", string.Empty)
                .Replace("\r", string.Empty)
                .Replace(" ", string.Empty);

            // Accept base64url variants
            s = s.Replace('-', '+').Replace('_', '/');

            // Pad to multiple of 4
            var mod4 = s.Length % 4;
            if (mod4 > 0)
            {
                s = s.PadRight(s.Length + (4 - mod4), '=');
            }

            return s;
        }

        private static int FirstInvalidBase64Index(string s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (!(char.IsLetterOrDigit(c) || c == '+' || c == '/' || c == '='))
                {
                    return i;
                }
            }

            return -1;
        }

        private static string Preview(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Length <= max ? s : s.Substring(0, max);
        }

        private static string PreviewTail(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Length <= max ? s : s.Substring(s.Length - max, max);
        }
    }
}
