using System;
using System.Collections.Generic;
using System.Reflection;
using AccessibilityMod.Core;
using UnityEngine;

namespace AccessibilityMod.Services
{
    /// <summary>
    /// Handles navigation for the Luminol spray mini-game where the player
    /// must find blood traces by spraying luminol on the scene.
    /// </summary>
    public static class LuminolNavigator
    {
        private static List<HotspotInfo> _hotspots = new List<HotspotInfo>();
        private static int _currentIndex = -1;
        private static bool _wasActive = false;

        public class HotspotInfo
        {
            public int Index;
            public float CenterX;
            public float CenterY;
            public string Description;
            public bool IsDiscovered;
        }

        /// <summary>
        /// Checks if the Luminol mini-game is currently active.
        /// </summary>
        public static bool IsLuminolActive()
        {
            try
            {
                if (luminolMiniGame.instance != null)
                {
                    // is_end is false when the mini-game is running
                    return !luminolMiniGame.instance.is_end;
                }
            }
            catch
            {
                // Class may not exist in current context
            }
            return false;
        }

        /// <summary>
        /// Called each frame to detect when luminol mode starts/ends.
        /// </summary>
        public static void Update()
        {
            bool isActive = IsLuminolActive();

            if (isActive && !_wasActive)
            {
                // Luminol mode just started
                OnLuminolStart();
            }
            else if (!isActive && _wasActive)
            {
                // Luminol mode just ended
                OnLuminolEnd();
            }

            _wasActive = isActive;
        }

        private static void OnLuminolStart()
        {
            RefreshHotspots();

            if (_hotspots.Count > 0)
            {
                int undiscoveredCount = 0;
                foreach (var h in _hotspots)
                {
                    if (!h.IsDiscovered)
                        undiscoveredCount++;
                }

                string message =
                    $"Luminol spray mode. {undiscoveredCount} blood trace{(undiscoveredCount != 1 ? "s" : "")} to find. Use [ and ] to navigate, Enter to spray.";
                ClipboardManager.Announce(message, TextType.Investigation);
            }
            else
            {
                ClipboardManager.Announce(
                    "Luminol spray mode. Use arrow keys to move cursor, Enter to spray, B to exit.",
                    TextType.Investigation
                );
            }
        }

        private static void OnLuminolEnd()
        {
            _hotspots.Clear();
            _currentIndex = -1;
        }

        /// <summary>
        /// Refreshes the list of hotspots from the game data.
        /// </summary>
        public static void RefreshHotspots()
        {
            _hotspots.Clear();
            _currentIndex = -1;

            try
            {
                if (luminolMiniGame.instance == null || luminolMiniGame.instance.is_end)
                    return;

                // Get converted_point_ which is public
                var convertedPoints = luminolMiniGame.instance.converted_point_;

                if (convertedPoints == null || convertedPoints.Count == 0)
                {
#if DEBUG
                    AccessibilityMod.Core.AccessibilityMod.Logger?.Msg(
                        "[Luminol] No converted points found"
                    );
#endif
                    return;
                }

                // Get active_blood_ via reflection to check discovered state
                var activeBloodField = typeof(luminolMiniGame).GetField(
                    "active_blood_",
                    BindingFlags.NonPublic | BindingFlags.Instance
                );

                List<luminolBloodstain> activeBlood = null;
                if (activeBloodField != null)
                {
                    activeBlood =
                        activeBloodField.GetValue(luminolMiniGame.instance)
                        as List<luminolBloodstain>;
                }

                for (int i = 0; i < convertedPoints.Count; i++)
                {
                    var point = convertedPoints[i];

                    // Skip empty/invalid points
                    if (point.x0 == 0 && point.y0 == 0 && point.x1 == 0 && point.y1 == 0)
                        continue;

                    // Calculate center of the quadrilateral
                    float centerX = (point.x0 + point.x1 + point.x2 + point.x3) / 4f;
                    float centerY = (point.y0 + point.y1 + point.y2 + point.y3) / 4f;

                    // Check if already discovered
                    bool isDiscovered = false;
                    if (activeBlood != null && i < activeBlood.Count)
                    {
                        isDiscovered = activeBlood[i].state_ != BloodstainState.Undiscovered;
                    }

                    // Generate position description
                    string posDesc = GetPositionDescription(centerX, centerY);
                    string status = isDiscovered ? " (found)" : "";

                    _hotspots.Add(
                        new HotspotInfo
                        {
                            Index = i,
                            CenterX = centerX,
                            CenterY = centerY,
                            Description = $"Blood trace {i + 1} ({posDesc}){status}",
                            IsDiscovered = isDiscovered,
                        }
                    );
                }

#if DEBUG
                AccessibilityMod.Core.AccessibilityMod.Logger?.Msg(
                    $"[Luminol] Found {_hotspots.Count} blood traces"
                );
#endif
            }
            catch (Exception ex)
            {
                AccessibilityMod.Core.AccessibilityMod.Logger?.Error(
                    $"Error refreshing luminol hotspots: {ex.Message}"
                );
            }
        }

        private static string GetPositionDescription(float x, float y)
        {
            // Screen is 1920x1080
            float areaWidth = 1920f;
            float areaHeight = 1080f;

            string horizontal =
                x < areaWidth * 0.33f ? "left"
                : x > areaWidth * 0.66f ? "right"
                : "center";
            string vertical =
                y < areaHeight * 0.33f ? "top"
                : y > areaHeight * 0.66f ? "bottom"
                : "middle";
            return $"{vertical} {horizontal}";
        }

        /// <summary>
        /// Navigate to the next undiscovered hotspot.
        /// </summary>
        public static void NavigateNext()
        {
            if (!IsLuminolActive())
            {
                ClipboardManager.Announce("Not in luminol mode", TextType.SystemMessage);
                return;
            }

            if (_hotspots.Count == 0)
            {
                RefreshHotspots();
            }

            if (_hotspots.Count == 0)
            {
                ClipboardManager.Announce("No blood traces found", TextType.Investigation);
                return;
            }

            // Find next undiscovered hotspot, wrapping around
            int startIndex = _currentIndex;
            int searchIndex = (_currentIndex + 1) % _hotspots.Count;
            bool foundUndiscovered = false;

            // First try to find an undiscovered one
            for (int i = 0; i < _hotspots.Count; i++)
            {
                int idx = (startIndex + 1 + i) % _hotspots.Count;
                if (!_hotspots[idx].IsDiscovered)
                {
                    _currentIndex = idx;
                    foundUndiscovered = true;
                    break;
                }
            }

            // If all are discovered, just go to next
            if (!foundUndiscovered)
            {
                _currentIndex = (_currentIndex + 1) % _hotspots.Count;
            }

            AnnounceCurrentHotspot();
            MoveCursorToCurrentHotspot();
        }

        /// <summary>
        /// Navigate to the previous undiscovered hotspot.
        /// </summary>
        public static void NavigatePrevious()
        {
            if (!IsLuminolActive())
            {
                ClipboardManager.Announce("Not in luminol mode", TextType.SystemMessage);
                return;
            }

            if (_hotspots.Count == 0)
            {
                RefreshHotspots();
            }

            if (_hotspots.Count == 0)
            {
                ClipboardManager.Announce("No blood traces found", TextType.Investigation);
                return;
            }

            // Find previous undiscovered hotspot, wrapping around
            int startIndex = _currentIndex < 0 ? 0 : _currentIndex;
            bool foundUndiscovered = false;

            // First try to find an undiscovered one
            for (int i = 0; i < _hotspots.Count; i++)
            {
                int idx = (startIndex - 1 - i + _hotspots.Count * 2) % _hotspots.Count;
                if (!_hotspots[idx].IsDiscovered)
                {
                    _currentIndex = idx;
                    foundUndiscovered = true;
                    break;
                }
            }

            // If all are discovered, just go to previous
            if (!foundUndiscovered)
            {
                _currentIndex = (_currentIndex - 1 + _hotspots.Count) % _hotspots.Count;
            }

            AnnounceCurrentHotspot();
            MoveCursorToCurrentHotspot();
        }

        /// <summary>
        /// Announce the currently selected hotspot.
        /// </summary>
        public static void AnnounceCurrentHotspot()
        {
            if (_hotspots.Count == 0 || _currentIndex < 0 || _currentIndex >= _hotspots.Count)
            {
                ClipboardManager.Announce("No blood trace selected", TextType.Investigation);
                return;
            }

            var hotspot = _hotspots[_currentIndex];
            ClipboardManager.Announce(hotspot.Description, TextType.Investigation);
        }

        /// <summary>
        /// Move the cursor to the currently selected hotspot.
        /// </summary>
        private static void MoveCursorToCurrentHotspot()
        {
            if (_hotspots.Count == 0 || _currentIndex < 0 || _currentIndex >= _hotspots.Count)
                return;

            try
            {
                var hotspot = _hotspots[_currentIndex];

                // Get cursor_position_ field via reflection
                var cursorPosField = typeof(luminolMiniGame).GetField(
                    "cursor_position_",
                    BindingFlags.NonPublic | BindingFlags.Instance
                );

                if (cursorPosField != null)
                {
                    // Set cursor position
                    Vector3 newPos = new Vector3(hotspot.CenterX, hotspot.CenterY, 0f);
                    cursorPosField.SetValue(luminolMiniGame.instance, newPos);

                    // Call UpdateCursorPosition to apply the change
                    var updateMethod = typeof(luminolMiniGame).GetMethod(
                        "UpdateCursorPosition",
                        BindingFlags.NonPublic | BindingFlags.Instance
                    );

                    if (updateMethod != null)
                    {
                        updateMethod.Invoke(luminolMiniGame.instance, null);
                    }

#if DEBUG
                    AccessibilityMod.Core.AccessibilityMod.Logger?.Msg(
                        $"[Luminol] Moved cursor to ({hotspot.CenterX}, {hotspot.CenterY})"
                    );
#endif
                }
            }
            catch (Exception ex)
            {
                AccessibilityMod.Core.AccessibilityMod.Logger?.Error(
                    $"Error moving luminol cursor: {ex.Message}"
                );
            }
        }

        /// <summary>
        /// Called when a bloodstain is discovered to update state and announce.
        /// </summary>
        public static void OnBloodstainDiscovered(int index)
        {
            // Refresh to update discovered state
            RefreshHotspots();

            // Count remaining
            int remaining = 0;
            foreach (var h in _hotspots)
            {
                if (!h.IsDiscovered)
                    remaining++;
            }

            string message =
                remaining > 0
                    ? $"Blood trace found! {remaining} remaining."
                    : "Blood trace found! All traces discovered.";

            ClipboardManager.Announce(message, TextType.Investigation);
        }

        /// <summary>
        /// Gets the number of hotspots.
        /// </summary>
        public static int GetHotspotCount()
        {
            if (_hotspots.Count == 0 && IsLuminolActive())
            {
                RefreshHotspots();
            }
            return _hotspots.Count;
        }

        /// <summary>
        /// Gets the number of undiscovered hotspots.
        /// </summary>
        public static int GetUndiscoveredCount()
        {
            if (_hotspots.Count == 0 && IsLuminolActive())
            {
                RefreshHotspots();
            }

            int count = 0;
            foreach (var h in _hotspots)
            {
                if (!h.IsDiscovered)
                    count++;
            }
            return count;
        }

        /// <summary>
        /// Announce current state for I key press.
        /// </summary>
        public static void AnnounceState()
        {
            if (!IsLuminolActive())
            {
                ClipboardManager.Announce("Not in luminol mode", TextType.SystemMessage);
                return;
            }

            RefreshHotspots();

            int total = _hotspots.Count;
            int remaining = GetUndiscoveredCount();

            string message =
                $"Luminol spray mode. {remaining} of {total} blood trace{(total != 1 ? "s" : "")} remaining. Use [ and ] to navigate.";
            ClipboardManager.Announce(message, TextType.Investigation);
        }
    }
}
