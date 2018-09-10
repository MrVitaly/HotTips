﻿using HotTips.Engine;
using Microsoft.VisualStudio.Settings;
using Microsoft.VisualStudio.Shell;
using System;
using System.Collections.Generic;
using System.Linq;
using Task = System.Threading.Tasks.Task;

namespace HotTips
{
    public class VSTipHistoryManager
    {
        private static readonly string SVsSettingsPersistenceManagerGuid = "9B164E40-C3A2-4363-9BC5-EB4039DEF653";
        private static readonly string TIP_OF_THE_DAY_SETTINGS = "TipOfTheDay";
        private static readonly string TIP_HISTORY = "TipHistory";
        private static readonly string EXCLUDED_TIP_GROUPS = TIP_OF_THE_DAY_SETTINGS+"_ExcludedTipGroups";
        private static readonly string TIP_CADENCE = TIP_OF_THE_DAY_SETTINGS + "_Cadence";
        private static readonly string TIP_LAST_DISPLAY = TIP_OF_THE_DAY_SETTINGS + "_NextDisplay";
        private static readonly string EXCLUDED_TIP_LEVELS = TIP_OF_THE_DAY_SETTINGS + "_ExcludedTipLevels";
        private static readonly bool RoamSettings = true;

        private static VSTipHistoryManager _instance;

        private ISettingsManager SettingsManager;

        private List<TipHistoryInfo> _tipsSeen;

        private HashSet<string> _excludedTipGroups;
        private bool _solutionOpenedOnce;
        private HashSet<TipLevel> _excludedTipLevels;

        public VSTipHistoryManager()
        {
            // Note: Must instaniate on UI thread.
            ThreadHelper.ThrowIfNotOnUIThread();
            SettingsManager = (ISettingsManager)ServiceProvider.GlobalProvider.GetService(new Guid(SVsSettingsPersistenceManagerGuid));

            InitializeTipsSeen();
            
            // Pull tip history from VS settings store (Comma separated string of TipIDs)
            string tipHistory = GetTipHistoryFromSettingsStore();
            List<TipHistoryInfo> tipsInfo = new List<TipHistoryInfo>();
            if (!string.IsNullOrEmpty(tipHistory))
            {
                string[] tips = tipHistory.Split(';');
                foreach (string item in tips)
                {
                    tipsInfo.Add(new TipHistoryInfo(item));
                }
            }

            _tipsSeen = tipsInfo;

            SettingsManager.TryGetValue(EXCLUDED_TIP_GROUPS, out string excludedGroups);

            _excludedTipGroups = (!string.IsNullOrEmpty(excludedGroups))
                ? new HashSet<string>(excludedGroups.Split(','), StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            SettingsManager.TryGetValue(EXCLUDED_TIP_LEVELS, out string excludedTipLevels);
            _excludedTipLevels = new HashSet<TipLevel>();

            if (!string.IsNullOrEmpty(excludedTipLevels))
            {
                foreach (var level in excludedTipLevels.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (Enum.TryParse(level, out TipLevel value))
                    {
                        _excludedTipLevels.Add(value);
                    }
                }
            }
        }

        public static VSTipHistoryManager GetInstance()
        {
            return _instance ?? (_instance = new VSTipHistoryManager());
        }

        public bool HasTipBeenSeen(string globalTipId)
        {
            List<TipHistoryInfo> tipsSeen = GetTipHistory();
            return tipsSeen != null && tipsSeen.Exists(a=>a.GlobalTipId.Equals(globalTipId));
        }

        public List<TipHistoryInfo> GetTipHistory()
        {
            if (_tipsSeen == null) InitializeTipsSeen();
            return _tipsSeen;
        }

        private string GetTipHistoryFromSettingsStore()
        {
            string collectionPath = TIP_OF_THE_DAY_SETTINGS;
            SettingsManager.TryGetValue(collectionPath, out string tipHistoryRaw);
            return tipHistoryRaw;
        }

        private void InitializeTipsSeen()
        {
            // Pull tip history from VS settings store (Comma separated string of TipIDs)
            string tipHistory = GetTipHistoryFromSettingsStore();
            // TODO: Parse the Tip History
            // Deserialize the raw string stored in the registry.
            // Format = [TipId]:[Vote];[TipId]:[Vote]
            // eg. "ED001:1;SHL001:0"
            // Note: Vote field might be missing (esp if people have previously run the older version before voting was stored)
            _tipsSeen = new List<TipHistoryInfo>();
            //_tipsSeen = (!string.IsNullOrEmpty(tipHistory)) ? new List<string>(tipHistory.Split(',')) : new List<string>();
        }

        public void MarkTipAsSeen(string globalTipId)
        {
            // Back out if tip already seen
            List<TipHistoryInfo> tipsSeen = GetTipHistory();
            if (tipsSeen.Exists(a => a.GlobalTipId.Equals(globalTipId)))
            {
                // Item is already in the history. No need to add it again.
                return;
            }

            // Add tip ID to the tip history and persist to the VS settings store
            tipsSeen.Add(new TipHistoryInfo(globalTipId));

            UpdateVSSettings(tipsSeen);
        }

        public void UpdateTipVoteStatus(string globalTipId, TipLikeEnum currentTipVoteStatus)
        {
            if (_tipsSeen.Exists(a => a.GlobalTipId.Equals(globalTipId)))
            {
                ////Update the voting status in the tip history 
                _tipsSeen.Find(a => a.GlobalTipId.Equals(globalTipId)).TipLikeStatus = currentTipVoteStatus;
            }
            else
            {
                TipHistoryInfo tipObj1 = new TipHistoryInfo();
                tipObj1.GlobalTipId = globalTipId;
                tipObj1.TipLikeStatus = currentTipVoteStatus;

                _tipsSeen.Add(tipObj1);
            }
            ////Update the VS settings store with the latest tip history
            UpdateVSSettings(_tipsSeen);
        }

        private void UpdateVSSettings(List<TipHistoryInfo> tipsSeen)
        {
            // Update the VS settings store with the latest tip history
            string tipHistoryRaw = String.Join(";", tipsSeen);
            // Get Writable settings store and update value
            string collectionPath = TIP_OF_THE_DAY_SETTINGS;
            //ISettingsList settingsList = SettingsManager.GetOrCreateList(collectionPath, isMachineLocal: !RoamSettings);
            SettingsManager.SetValueAsync(collectionPath, tipHistoryRaw, isMachineLocal: !RoamSettings);
        }

        public void ClearTipHistory()
        {
            // Clear the local tips object
            _tipsSeen = new List<TipHistoryInfo>(); 

            // Delete the entries from the Settings Store by setting to Empty string
            string collectionPath = TIP_OF_THE_DAY_SETTINGS;
            SettingsManager.SetValueAsync(collectionPath, string.Empty, isMachineLocal: !RoamSettings);
        }

        public bool IsTipGroupExcluded(string tipGroupId)
        {
            return _excludedTipGroups.Contains(tipGroupId);
        }

        public void MarkTipGroupAsExcluded(string tipGroupId)
        {
            _excludedTipGroups.Add(tipGroupId);
            StoreExcludedGroupsToSettings();
        }

        private void StoreExcludedGroupsToSettings()
        {
            string value = string.Join(",", _excludedTipGroups);
            SettingsManager.SetValueAsync(EXCLUDED_TIP_GROUPS, value, isMachineLocal: !RoamSettings);
        }

        public void MarkTipGroupAsIncluded(string tipGroupId)
        {
            _excludedTipGroups.Remove(tipGroupId);
            StoreExcludedGroupsToSettings();
        }

        public async Task SetCadenceAsync(DisplayCadence cadence)
        {
            await SettingsManager.SetValueAsync(TIP_CADENCE, cadence.Name, isMachineLocal: true);
        }

        public DisplayCadence GetCadence()
        {
            var cadenceString = SettingsManager.GetValueOrDefault(TIP_CADENCE, string.Empty);
            return string.IsNullOrEmpty(cadenceString) ? DisplayCadence.Startup : DisplayCadence.FromName(cadenceString);
        }

        public async Task SetLastDisplayTimeNowAsync() => await SetLastDisplayTimeAsync(DateTime.UtcNow);

        public async Task SetLastDisplayTimeAsync(DateTime dateTime)
        {
            await SettingsManager.SetValueAsync(TIP_LAST_DISPLAY, dateTime, isMachineLocal: true);
        }

        public DateTime GetLastDisplayTime()
        {
            return SettingsManager.GetValueOrDefault(TIP_LAST_DISPLAY, DateTime.UtcNow);
        }

        public bool ShouldShowTip(DisplayCadence cadence)
        {
            var localLastDisplayTime = GetLastDisplayTime().ToLocalTime();
            var localStartOfDay = localLastDisplayTime.Date;
            var withDelay = localStartOfDay + cadence.Delay;
            var appropriateShowTime = withDelay.ToUniversalTime();
            return DateTime.UtcNow >= appropriateShowTime;
        }

        public void HandleVsInitialized()
        {
            var cadence = GetCadence();
            if (cadence == DisplayCadence.Never || cadence == DisplayCadence.SolutionLoad)
                return;

            if (ShouldShowTip(cadence))
            {
                TipOfTheDay.ShowWindow();
                Task.Run(async () => await SetLastDisplayTimeNowAsync());
            }
        }

        public void HandleSolutionOpened()
        {
            var cadence = GetCadence();

            // Allow one operation when cadence is Startup and multiple when cadence is SolutionLoad
            if (!(cadence == DisplayCadence.Startup && !_solutionOpenedOnce)
                && !(cadence == DisplayCadence.SolutionLoad))
                return;

            if (ShouldShowTip(cadence))
            {
                TipOfTheDay.ShowWindow();
                _solutionOpenedOnce = true; // When DisplayCadence is Startup, show tip only once
                Task.Run(async () => await SetLastDisplayTimeNowAsync());
            }
        }

        public bool IsTipLevelExcluded(TipLevel level)
        {
            return _excludedTipLevels.Contains(level);
        }

        public void MarkTipLevelAsIncluded(TipLevel level)
        {
            _excludedTipLevels.Remove(level);
            StoreExcludedLevelsToSettings();
        }

        public void MarkTipLevelAsExcluded(TipLevel level)
        {
            _excludedTipLevels.Add(level);
            StoreExcludedLevelsToSettings();
        }

        private void StoreExcludedLevelsToSettings()
        {
            string value = string.Join(",", _excludedTipLevels.Select(l => l.ToString()));
            SettingsManager.SetValueAsync(EXCLUDED_TIP_LEVELS, value, isMachineLocal: !RoamSettings);
        }

        public ISet<string> GetAllExcludedTipGroups()
        {
            return _excludedTipGroups;
        }

        public ISet<TipLevel> GetAllExcludedTipLevels()
        {
            return _excludedTipLevels;
        }
    }
}