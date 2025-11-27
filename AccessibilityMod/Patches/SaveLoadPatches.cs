using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine.UI;
using AccessibilityMod.Core;

namespace AccessibilityMod.Patches
{
    [HarmonyPatch]
    public static class SaveLoadPatches
    {
        private static int _lastSlotCursor = -1;

        // Hook when save/load UI opens
        [HarmonyPostfix]
        [HarmonyPatch(typeof(SaveLoadUICtrl), "Open")]
        public static void Open_Postfix(SaveLoadUICtrl __instance)
        {
            try
            {
                var slotType = GetSlotType(__instance);
                string typeName = slotType == 0 ? "Save" : "Load";

                ClipboardManager.Announce($"{typeName} menu opened", TextType.Menu);
                _lastSlotCursor = -1;
            }
            catch (Exception ex)
            {
                AccessibilityMod.Core.AccessibilityMod.Logger?.Error($"Error in SaveLoad Open patch: {ex.Message}");
            }
        }

        // Hook cursor changes in save/load via UpdateCursorPosition
        [HarmonyPostfix]
        [HarmonyPatch(typeof(SaveLoadUICtrl), "UpdateCursorPosition")]
        public static void UpdateCursorPosition_Postfix(SaveLoadUICtrl __instance)
        {
            try
            {
                int currentSlot = GetCurrentSlot(__instance);
                if (currentSlot != _lastSlotCursor && currentSlot >= 0)
                {
                    _lastSlotCursor = currentSlot;
                    AnnounceSlot(__instance, currentSlot);
                }
            }
            catch (Exception ex)
            {
                AccessibilityMod.Core.AccessibilityMod.Logger?.Error($"Error in SaveLoad UpdateCursorPosition patch: {ex.Message}");
            }
        }

        private static int GetCurrentSlot(SaveLoadUICtrl ctrl)
        {
            try
            {
                var field = typeof(SaveLoadUICtrl).GetField("select_num_",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (field != null)
                {
                    return (int)field.GetValue(ctrl);
                }
            }
            catch { }
            return -1;
        }

        private static void AnnounceSlot(SaveLoadUICtrl ctrl, int slotNo)
        {
            try
            {
                // Simple slot announcement - slot number (1-10)
                string slotInfo = $"Slot {slotNo + 1}";

                // Try to get more info via reflection if available
                try
                {
                    var slotListField = typeof(SaveLoadUICtrl).GetField("slot_list_",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                    if (slotListField != null)
                    {
                        var slotList = slotListField.GetValue(ctrl) as System.Collections.IList;
                        if (slotList != null && slotNo >= 0 && slotNo < slotList.Count)
                        {
                            var slot = slotList[slotNo];
                            // Try to get slot text or status
                            var textField = slot.GetType().GetField("text_");
                            if (textField != null)
                            {
                                var text = textField.GetValue(slot) as UnityEngine.UI.Text;
                                if (text != null && !string.IsNullOrEmpty(text.text))
                                {
                                    slotInfo = $"Slot {slotNo + 1}: {text.text}";
                                }
                            }
                        }
                    }
                }
                catch { }

                ClipboardManager.Announce(slotInfo, TextType.Menu);
            }
            catch (Exception ex)
            {
                AccessibilityMod.Core.AccessibilityMod.Logger?.Error($"Error announcing slot: {ex.Message}");
            }
        }

        private static int GetSlotType(SaveLoadUICtrl ctrl)
        {
            try
            {
                var field = typeof(SaveLoadUICtrl).GetField("slot_type_",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (field != null)
                {
                    return (int)field.GetValue(ctrl);
                }
            }
            catch { }
            return 0;
        }
    }

    [HarmonyPatch]
    public static class OptionPatches
    {
        private static int _lastCategory = -1;
        private static int _lastOptionIndex = -1;

        // Reflection cache
        private static FieldInfo _currentNumField;
        private static FieldInfo _availableOptionField;
        private static FieldInfo _optionTitleField;

        private static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;

        // Hook when options menu category changes
        [HarmonyPostfix]
        [HarmonyPatch(typeof(optionCtrl), "ChangeCategory")]
        public static void ChangeCategory_Postfix(optionCtrl __instance, optionCtrl.Category cat)
        {
            try
            {
                int categoryInt = (int)cat;
                if (categoryInt != _lastCategory)
                {
                    _lastCategory = categoryInt;
                    _lastOptionIndex = -1; // Reset option index when category changes

                    string categoryName = GetCategoryName(cat);
                    ClipboardManager.Announce($"Options: {categoryName}", TextType.Menu);
                }
            }
            catch (Exception ex)
            {
                AccessibilityMod.Core.AccessibilityMod.Logger?.Error($"Error in Options ChangeCategory patch: {ex.Message}");
            }
        }

        // Hook when navigating between options within a category
        [HarmonyPostfix]
        [HarmonyPatch(typeof(optionCtrl), "SelectItemSet")]
        public static void SelectItemSet_Postfix(optionCtrl __instance)
        {
            try
            {
                int currentIndex = GetCurrentOptionIndex(__instance);
                if (currentIndex == _lastOptionIndex || currentIndex < 0)
                    return;

                _lastOptionIndex = currentIndex;

                var options = GetAvailableOptions(__instance);
                if (options == null || currentIndex >= options.Count)
                    return;

                var currentOption = options[currentIndex];
                string optionName = GetOptionName(currentOption);
                string currentValue = GetOptionValue(currentOption);

                string message;
                if (!Net35Extensions.IsNullOrWhiteSpace(currentValue))
                    message = $"{optionName}: {currentValue}";
                else
                    message = optionName;

                ClipboardManager.Announce(message, TextType.Menu);
            }
            catch (Exception ex)
            {
                AccessibilityMod.Core.AccessibilityMod.Logger?.Error($"Error in Options SelectItemSet patch: {ex.Message}");
            }
        }

        // Hook value changes for gauge-type options (BGM, SE)
        [HarmonyPostfix]
        [HarmonyPatch(typeof(optionBgm), "ChangeValue")]
        public static void OptionBgm_ChangeValue_Postfix(optionItem __instance)
        {
            AnnounceOptionValue(__instance);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(optionSe), "ChangeValue")]
        public static void OptionSe_ChangeValue_Postfix(optionItem __instance)
        {
            AnnounceOptionValue(__instance);
        }

        // Hook value changes for toggle-type options
        [HarmonyPostfix]
        [HarmonyPatch(typeof(optionToggleItem), "ChangeValue")]
        public static void OptionToggle_ChangeValue_Postfix(optionItem __instance)
        {
            AnnounceOptionValue(__instance);
        }

        // Hook value changes for skip option
        [HarmonyPostfix]
        [HarmonyPatch(typeof(optionSkip), "ChangeValue")]
        public static void OptionSkip_ChangeValue_Postfix(optionItem __instance)
        {
            AnnounceOptionValue(__instance);
        }

        // Hook value changes for window transparency option
        [HarmonyPostfix]
        [HarmonyPatch(typeof(optionWindow), "ChangeValue")]
        public static void OptionWindow_ChangeValue_Postfix(optionItem __instance)
        {
            AnnounceOptionValue(__instance);
        }

        // Hook value changes for vibration option (extends optionItem directly)
        [HarmonyPostfix]
        [HarmonyPatch(typeof(optionVibration), "ChangeValue")]
        public static void OptionVibration_ChangeValue_Postfix(optionItem __instance)
        {
            AnnounceOptionValue(__instance);
        }

        // Hook value changes for select-type options (language, resolution, window mode, auto-speed)
        [HarmonyPostfix]
        [HarmonyPatch(typeof(optionSelectItem), "ChangeValue")]
        public static void OptionSelect_ChangeValue_Postfix(optionItem __instance)
        {
            AnnounceOptionValue(__instance);
        }

        private static void AnnounceOptionValue(optionItem item)
        {
            try
            {
                string value = GetOptionValue(item);
                if (!Net35Extensions.IsNullOrWhiteSpace(value))
                {
                    ClipboardManager.Announce(value, TextType.Menu);
                }
            }
            catch (Exception ex)
            {
                AccessibilityMod.Core.AccessibilityMod.Logger?.Error($"Error announcing option value: {ex.Message}");
            }
        }

        private static int GetCurrentOptionIndex(optionCtrl ctrl)
        {
            try
            {
                if (_currentNumField == null)
                    _currentNumField = typeof(optionCtrl).GetField("current_num_", NonPublicInstance);

                if (_currentNumField != null)
                    return (int)_currentNumField.GetValue(ctrl);
            }
            catch { }
            return -1;
        }

        private static List<optionItem> GetAvailableOptions(optionCtrl ctrl)
        {
            try
            {
                if (_availableOptionField == null)
                    _availableOptionField = typeof(optionCtrl).GetField("available_option_", NonPublicInstance);

                if (_availableOptionField != null)
                    return _availableOptionField.GetValue(ctrl) as List<optionItem>;
            }
            catch { }
            return null;
        }

        private static string GetOptionName(optionItem item)
        {
            try
            {
                if (_optionTitleField == null)
                    _optionTitleField = typeof(optionItem).GetField("option_title_", NonPublicInstance);

                if (_optionTitleField != null)
                {
                    var titleText = _optionTitleField.GetValue(item) as Text;
                    if (titleText != null && !Net35Extensions.IsNullOrWhiteSpace(titleText.text))
                        return titleText.text;
                }
            }
            catch { }
            return "Unknown option";
        }

        private static string GetOptionValue(optionItem item)
        {
            try
            {
                Type itemType = item.GetType();

                // Try gauge type pattern first (optionBgm, optionSe)
                // These have gauge_ field with count_text_.text
                var gaugeField = FindField(itemType, "gauge_");
                if (gaugeField != null)
                {
                    var gauge = gaugeField.GetValue(item);
                    if (gauge != null)
                    {
                        var countTextField = gauge.GetType().GetField("count_text_");
                        if (countTextField != null)
                        {
                            var countText = countTextField.GetValue(gauge) as Text;
                            if (countText != null && !Net35Extensions.IsNullOrWhiteSpace(countText.text))
                            {
                                return countText.text;
                            }
                        }
                    }
                }

                // Try toggle/select type pattern (optionToggleItem, optionSkip, optionShake, optionWindow, etc.)
                // These have select_text_[] array and setting_value_ index
                var selectTextField = FindField(itemType, "select_text_");
                var settingValueField = FindField(itemType, "setting_value_");

                if (selectTextField != null && settingValueField != null)
                {
                    var selectTexts = selectTextField.GetValue(item) as string[];
                    int settingValue = (int)settingValueField.GetValue(item);
                    if (selectTexts != null && settingValue >= 0 && settingValue < selectTexts.Length)
                    {
                        return selectTexts[settingValue];
                    }
                }

                // Fallback: try to get setting_value_ as a number
                if (settingValueField != null)
                {
                    int settingValue = (int)settingValueField.GetValue(item);
                    return settingValue.ToString();
                }
            }
            catch { }
            return null;
        }

        // Helper to find a field in a type or its base types
        private static FieldInfo FindField(Type type, string fieldName)
        {
            while (type != null && type != typeof(object))
            {
                var field = type.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                if (field != null)
                    return field;
                type = type.BaseType;
            }
            return null;
        }

        private static string GetCategoryName(optionCtrl.Category category)
        {
            switch (category)
            {
                case optionCtrl.Category.SAVE_LOAD: return "Save/Load";
                case optionCtrl.Category.SOUND: return "Sound";
                case optionCtrl.Category.GAME: return "Game";
                case optionCtrl.Category.LANGUAGE: return "Language";
                case optionCtrl.Category.PC: return "Display";
                case optionCtrl.Category.KEYCONFIG: return "Key Config";
                case optionCtrl.Category.STORY: return "Story";
                case optionCtrl.Category.CREDIT: return "Credits";
                case optionCtrl.Category.PRIVACY: return "Privacy";
                default: return category.ToString();
            }
        }
    }

}
