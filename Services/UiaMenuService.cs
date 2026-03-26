using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace MenuBar.Services
{
    /// <summary>
    /// Discovers and invokes menu items for apps that expose a menu bar through
    /// UI Automation but have no Win32 HMENU (e.g. WPF apps, VS Code, Win11 Notepad).
    ///
    /// Chromium-based browsers (Edge, Chrome) have no structured menu at all —
    /// UIA will return null for those and the panel stays empty.
    /// </summary>
    public static class UiaMenuService
    {
        // ── UIA constants ────────────────────────────────────────────────────────
        private const int UIA_ControlTypePropertyId    = 30003;
        private const int UIA_MenuBarControlTypeId     = 50011;
        private const int UIA_MenuItemControlTypeId    = 50012;
        private const int UIA_ButtonControlTypeId      = 50000;
        private const int UIA_ExpandCollapsePatternId  = 10005;
        private const int UIA_InvokePatternId          = 10000;

        private const int TreeScope_Children    = 2;
        private const int TreeScope_Descendants = 4;

        // ── Public data ──────────────────────────────────────────────────────────
        public sealed class UiaMenuItem
        {
            public string Label;
            /// <summary>
            /// The raw IUIAutomationElement COM object (typed as object to keep the
            /// COM interfaces private). Pass to ExpandOrInvokeMenuItem().
            /// </summary>
            public object Element;
            public bool HasSubMenu;
        }

        // ── COM interface definitions (vtable-exact, minimal subset) ─────────────
        //
        // Method order must match UIAutomationClient.h exactly, with IUnknown
        // (QueryInterface/AddRef/Release) omitted — the CLR adds them implicitly.

        [ComImport, Guid("352ffba8-0973-437c-a61f-f64cafd81df9"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IUIAutomationCondition { }

        [ComImport, Guid("14314595-b4bc-4055-95f2-58f2e42c9855"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IUIAutomationElementArray
        {
            // slot 0
            [PreserveSig] int get_Length(out int length);
            // slot 1
            [PreserveSig] int GetElement(int index, out IUIAutomationElement element);
        }

        [ComImport, Guid("d22108aa-8ac5-49a5-837b-37bbb3d7591e"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IUIAutomationElement
        {
            // 0: SetFocus
            [PreserveSig] int SetFocus();
            // 1: GetRuntimeId — not used; IntPtr placeholders keep slot alignment
            [PreserveSig] int GetRuntimeId(out IntPtr runtimeId);
            // 2: FindFirst
            [PreserveSig] int FindFirst(int scope, IUIAutomationCondition condition,
                out IUIAutomationElement found);
            // 3: FindAll
            [PreserveSig] int FindAll(int scope, IUIAutomationCondition condition,
                out IUIAutomationElementArray found);
            // 4: FindFirstBuildCache — not used
            [PreserveSig] int FindFirstBuildCache(int scope, IUIAutomationCondition condition,
                IntPtr cacheRequest, out IUIAutomationElement found);
            // 5: FindAllBuildCache — not used
            [PreserveSig] int FindAllBuildCache(int scope, IUIAutomationCondition condition,
                IntPtr cacheRequest, out IUIAutomationElementArray found);
            // 6: BuildUpdatedCache — not used
            [PreserveSig] int BuildUpdatedCache(IntPtr cacheRequest, out IUIAutomationElement updated);
            // 7: GetCurrentPropertyValue — not used
            [PreserveSig] int GetCurrentPropertyValue(int propertyId,
                [MarshalAs(UnmanagedType.Struct)] out object value);
            // 8: GetCurrentPropertyValueEx — not used
            [PreserveSig] int GetCurrentPropertyValueEx(int propertyId, bool ignoreDefault,
                [MarshalAs(UnmanagedType.Struct)] out object value);
            // 9: GetCachedPropertyValue — not used
            [PreserveSig] int GetCachedPropertyValue(int propertyId,
                [MarshalAs(UnmanagedType.Struct)] out object value);
            // 10: GetCachedPropertyValueEx — not used
            [PreserveSig] int GetCachedPropertyValueEx(int propertyId, bool ignoreDefault,
                [MarshalAs(UnmanagedType.Struct)] out object value);
            // 11: GetCurrentPattern
            [PreserveSig] int GetCurrentPattern(int patternId,
                [MarshalAs(UnmanagedType.IUnknown)] out object pattern);
            // 12: GetCachedPattern — not used
            [PreserveSig] int GetCachedPattern(int patternId,
                [MarshalAs(UnmanagedType.IUnknown)] out object pattern);
            // 13: GetCachedParent — not used
            [PreserveSig] int GetCachedParent(out IUIAutomationElement parent);
            // 14: GetCachedChildren — not used
            [PreserveSig] int GetCachedChildren(out IUIAutomationElementArray children);
            // 15: get_CurrentProcessId — not used
            [PreserveSig] int get_CurrentProcessId(out int retVal);
            // 16: get_CurrentControlType
            [PreserveSig] int get_CurrentControlType(out int retVal);
            // 17: get_CurrentLocalizedControlType — not used
            [PreserveSig] int get_CurrentLocalizedControlType(
                [MarshalAs(UnmanagedType.BStr)] out string retVal);
            // 18: get_CurrentName
            [PreserveSig] int get_CurrentName([MarshalAs(UnmanagedType.BStr)] out string retVal);
            // Slots 19+ not needed; CLR stops vtable thunks here.
        }

        [ComImport, Guid("619be086-1f4e-4ee4-bafa-210128738730"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IUIAutomationExpandCollapsePattern
        {
            [PreserveSig] int Expand();
            [PreserveSig] int Collapse();
            [PreserveSig] int get_CurrentExpandCollapseState(out int retVal);
            [PreserveSig] int get_CachedExpandCollapseState(out int retVal);
        }

        [ComImport, Guid("54fcb24b-e18e-47a2-b4d3-eccbe77599a2"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IUIAutomationInvokePattern
        {
            [PreserveSig] int Invoke();
        }

        // IUIAutomation vtable slots 0–20 (CompareElements … CreatePropertyCondition).
        // Slots after 20 are not declared; we never call them.
        [StructLayout(LayoutKind.Sequential)]
        private struct UIA_POINT { public int X; public int Y; }

        [ComImport, Guid("30cbe57d-d9d0-452a-ab13-7ac5ac4825ee"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IUIAutomation
        {
            // 0: CompareElements
            [PreserveSig] int CompareElements(IUIAutomationElement el1,
                IUIAutomationElement el2, out bool areSame);
            // 1: CompareRuntimeIds — IntPtr placeholders
            [PreserveSig] int CompareRuntimeIds(IntPtr id1, IntPtr id2, out bool areSame);
            // 2: GetRootElement
            [PreserveSig] int GetRootElement(out IUIAutomationElement root);
            // 3: ElementFromHandle
            [PreserveSig] int ElementFromHandle(IntPtr hwnd, out IUIAutomationElement element);
            // 4: ElementFromPoint
            [PreserveSig] int ElementFromPoint(UIA_POINT pt, out IUIAutomationElement element);
            // 5: GetFocusedElement
            [PreserveSig] int GetFocusedElement(out IUIAutomationElement element);
            // 6: GetRootElementBuildCache
            [PreserveSig] int GetRootElementBuildCache(IntPtr cacheRequest,
                out IUIAutomationElement root);
            // 7: ElementFromHandleBuildCache
            [PreserveSig] int ElementFromHandleBuildCache(IntPtr hwnd, IntPtr cacheRequest,
                out IUIAutomationElement element);
            // 8: ElementFromPointBuildCache
            [PreserveSig] int ElementFromPointBuildCache(UIA_POINT pt, IntPtr cacheRequest,
                out IUIAutomationElement element);
            // 9: GetFocusedElementBuildCache
            [PreserveSig] int GetFocusedElementBuildCache(IntPtr cacheRequest,
                out IUIAutomationElement element);
            // 10: CreateTreeWalker
            [PreserveSig] int CreateTreeWalker(IUIAutomationCondition condition,
                [MarshalAs(UnmanagedType.IUnknown)] out object walker);
            // 11: get_ControlViewWalker
            [PreserveSig] int get_ControlViewWalker(
                [MarshalAs(UnmanagedType.IUnknown)] out object walker);
            // 12: get_ContentViewWalker
            [PreserveSig] int get_ContentViewWalker(
                [MarshalAs(UnmanagedType.IUnknown)] out object walker);
            // 13: get_RawViewWalker
            [PreserveSig] int get_RawViewWalker(
                [MarshalAs(UnmanagedType.IUnknown)] out object walker);
            // 14: get_RawViewCondition
            [PreserveSig] int get_RawViewCondition(out IUIAutomationCondition condition);
            // 15: get_ControlViewCondition
            [PreserveSig] int get_ControlViewCondition(out IUIAutomationCondition condition);
            // 16: get_ContentViewCondition
            [PreserveSig] int get_ContentViewCondition(out IUIAutomationCondition condition);
            // 17: CreateCacheRequest
            [PreserveSig] int CreateCacheRequest(
                [MarshalAs(UnmanagedType.IUnknown)] out object cacheRequest);
            // 18: CreateTrueCondition
            [PreserveSig] int CreateTrueCondition(out IUIAutomationCondition condition);
            // 19: CreateFalseCondition
            [PreserveSig] int CreateFalseCondition(out IUIAutomationCondition condition);
            // 20: CreatePropertyCondition — value is marshaled as VARIANT
            [PreserveSig] int CreatePropertyCondition(int propertyId,
                [MarshalAs(UnmanagedType.Struct)] object value,
                out IUIAutomationCondition condition);
            // Slots 21+ not declared.
        }

        // ── Factory (lazy, singleton per-process) ────────────────────────────────
        private static IUIAutomation _uia;
        private static IUIAutomationCondition _trueCondition;
        private static bool _initFailed;

        private static IUIAutomation GetUia()
        {
            if (_uia != null) return _uia;
            if (_initFailed) return null;
            try
            {
                // CUIAutomation8 CLSID — available on Windows 8+ (we target 10 1809+)
                var t = Type.GetTypeFromCLSID(new Guid("e22ad333-b25f-460c-83d0-0581107395c9"));
                _uia = (IUIAutomation)Activator.CreateInstance(t);
                _uia.CreateTrueCondition(out _trueCondition);
                return _uia;
            }
            catch
            {
                _initFailed = true;
                return null;
            }
        }

        // ── Public API ───────────────────────────────────────────────────────────

        /// <summary>
        /// Returns top-level menu items from the UIA accessibility tree of <paramref name="hwnd"/>.
        /// Runs on a background thread to prevent UI hangs.
        /// </summary>
        public static Task<List<UiaMenuItem>> GetMenuItemsAsync(IntPtr hwnd)
        {
            return Task.Run(() => GetMenuItems(hwnd));
        }

        /// <summary>
        /// Returns top-level menu items from the UIA accessibility tree of <paramref name="hwnd"/>.
        /// Returns null when no MenuBar is found (most Chromium apps, apps with no AT support).
        /// </summary>
        private static List<UiaMenuItem> GetMenuItems(IntPtr hwnd)
        {
            try
            {
                IUIAutomation uia = GetUia();
                if (uia == null) return null;

                if (uia.ElementFromHandle(hwnd, out IUIAutomationElement root) != 0 || root == null)
                    return null;

                // Build condition: ControlType == MenuBar
                if (uia.CreatePropertyCondition(UIA_ControlTypePropertyId,
                        (object)(int)UIA_MenuBarControlTypeId, out IUIAutomationCondition mbCond) != 0
                    || mbCond == null)
                    return null;

                // Search for a MenuBar descendant (most apps have it one or two levels deep)
                if (root.FindFirst(TreeScope_Descendants, mbCond,
                        out IUIAutomationElement menuBar) != 0
                    || menuBar == null)
                    return null;

                // Get all direct children of the MenuBar
                if (_trueCondition == null) return null;
                if (menuBar.FindAll(TreeScope_Children, _trueCondition,
                        out IUIAutomationElementArray children) != 0
                    || children == null)
                    return null;

                children.get_Length(out int count);
                if (count <= 0) return null;

                var items = new List<UiaMenuItem>(count);
                for (int i = 0; i < count; i++)
                {
                    if (children.GetElement(i, out IUIAutomationElement child) != 0 || child == null)
                        continue;

                    child.get_CurrentControlType(out int ct);
                    // Accept MenuItem (50012) and Button (50000) — Win11 Notepad uses Button
                    if (ct != UIA_MenuItemControlTypeId && ct != UIA_ButtonControlTypeId)
                        continue;

                    child.get_CurrentName(out string name);
                    name = name?.Trim();
                    if (string.IsNullOrWhiteSpace(name)) continue;

                    // Determine whether ExpandCollapse or Invoke should be used on click
                    bool hasSubMenu = ct == UIA_MenuItemControlTypeId;
                    if (!hasSubMenu)
                    {
                        // Button type — check if it supports ExpandCollapse
                        child.GetCurrentPattern(UIA_ExpandCollapsePatternId, out object ecPat);
                        hasSubMenu = ecPat != null;
                    }

                    items.Add(new UiaMenuItem
                    {
                        Label = name,
                        Element = child,
                        HasSubMenu = hasSubMenu
                    });
                }

                return items.Count > 0 ? items : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Opens the menu associated with <paramref name="element"/> by calling
        /// ExpandCollapsePattern.Expand(), or falls back to InvokePattern.Invoke()
        /// for leaf items. The app's native dropdown appears in the target app window.
        /// </summary>
        public static void ExpandOrInvokeMenuItem(object element)
        {
            try
            {
                if (element is not IUIAutomationElement el) return;

                // Try ExpandCollapse first (top-level menu items with submenus)
                if (el.GetCurrentPattern(UIA_ExpandCollapsePatternId, out object ecObj) == 0
                    && ecObj is IUIAutomationExpandCollapsePattern ec)
                {
                    ec.Expand();
                    return;
                }

                // Fallback: Invoke (leaf menu items or toolbar buttons)
                if (el.GetCurrentPattern(UIA_InvokePatternId, out object invObj) == 0
                    && invObj is IUIAutomationInvokePattern inv)
                {
                    inv.Invoke();
                }
            }
            catch { }
        }
    }
}
