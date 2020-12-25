using System;
using System.Runtime.InteropServices;
using Dalamud.Game.ClientState.Structs;
using Dalamud.Hooking;
using Dalamud.Plugin;
using Lumina.Excel.GeneratedSheets;
using Microsoft.Win32;
using TabTab.Attributes;

namespace TabTab
{
    public class Plugin : IDalamudPlugin
    {
        private DalamudPluginInterface pluginInterface;
        private PluginCommandManager<Plugin> commandManager;
        private Configuration config;
        private PluginUI ui;
        
        private delegate IntPtr SelectInitialTabTarget(IntPtr targetSystem, IntPtr gameObjects, IntPtr camera, IntPtr a4);

        private delegate IntPtr SelectTabTarget(IntPtr targetSystem, IntPtr camera, IntPtr gameObjects, bool inverse,
            IntPtr a5);
        
        private delegate int TargetSortComparator(IntPtr a1, IntPtr a2);
        private delegate IntPtr OnTabTarget(IntPtr a1, IntPtr a2, IntPtr a3, IntPtr a4);
        
        public string Name => "TabTab";

        private Hook<SelectTabTarget> tabIgnoreDepthHook;
        private Hook<SelectTabTarget> tabConeHook;
        
        private Hook<SelectInitialTabTarget> selectInitialTabTargetHook;
        private Hook<TargetSortComparator> targetSortComparatorHook;
        private Hook<OnTabTarget> tabTargetHook;

        public bool Enable = true;
        public int Mode = 1;

        public void Initialize(DalamudPluginInterface pluginInterface)
        {
            this.pluginInterface = pluginInterface;

            this.config = (Configuration)this.pluginInterface.GetPluginConfig() ?? new Configuration();
            this.config.Initialize(this.pluginInterface);
            
            // Load transient config vars
            this.Enable = this.config.Enabled;
            this.Mode = this.config.Mode;

            this.ui = new PluginUI(this);
            this.pluginInterface.UiBuilder.OnBuildUi += this.ui.Draw;

            var tabIgnoreDepthAddr =
                this.pluginInterface.TargetModuleScanner.ScanText("E8 ?? ?? ?? ?? 48 8B C8 48 85 C0 74 27");
            var tabConeAddr = this.pluginInterface.TargetModuleScanner.ScanText("E8 ?? ?? ?? ?? EB 4C 41 B1 01");
            PluginLog.Log($"Found SelectTabTarget mode addresses: {tabIgnoreDepthAddr.ToInt64():X}, {tabConeAddr.ToInt64():X}");

            this.tabIgnoreDepthHook ??=
                new Hook<SelectTabTarget>(tabIgnoreDepthAddr, new SelectTabTarget(SelectTabTargetIgnoreDepthDetour));
            this.tabIgnoreDepthHook.Enable();
            
            this.tabConeHook ??=
                new Hook<SelectTabTarget>(tabConeAddr, new SelectTabTarget(SelectTabTargetConeDetour));
            this.tabConeHook.Enable();
            
            var selectInitialTabTargetAddr =
                this.pluginInterface.TargetModuleScanner.ScanText("E8 ?? ?? ?? ?? EB 37 48 85 C9");
            PluginLog.Log($"Found SelectInitialTabTarget address: {selectInitialTabTargetAddr.ToInt64():X}");
            this.selectInitialTabTargetHook ??= new Hook<SelectInitialTabTarget>(selectInitialTabTargetAddr,
                new SelectInitialTabTarget(SelectInitialTabTargetDetour));
            this.selectInitialTabTargetHook.Enable();
            
            var targetSortComparatorAddr =
                this.pluginInterface.TargetModuleScanner.ScanText("40 53 48 83 EC 20 F3 0F 10 01 48 8B D9 F3 0F 10");
            PluginLog.Log($"Found TargetSortComparator address: {targetSortComparatorAddr.ToInt64():X}");
            this.targetSortComparatorHook ??= new Hook<TargetSortComparator>(targetSortComparatorAddr,
                new TargetSortComparator(TargetSortComparatorDetour));
            this.targetSortComparatorHook.Enable();
            
            // No longer needed sigs, but used for testing in dev
            // (as of 5.4 hotfix)
            // TargetSelect: E8 ?? ?? ?? ?? 44 0F B6 C3 48 8B D0
            // SwitchTarget: E8 ?? ?? ?? ?? 48 39 B7 ?? ?? ?? ?? 74 6A
            // TargetSortComparator for cone mode: 48 83 EC 28 F3 0F 10 01

            var tabTargetAddr =
                this.pluginInterface.TargetModuleScanner.ScanText("41 54 41 56 41 57 48 81 EC ?? ?? ?? ?? 8B 81 ?? ?? ?? ??");
            PluginLog.Log($"Found TabTarget address: {tabTargetAddr.ToInt64():X}");
            this.tabTargetHook ??= new Hook<OnTabTarget>(tabTargetAddr, new OnTabTarget(OnTabTargetDetour));
            this.tabTargetHook.Enable();
            
            this.commandManager = new PluginCommandManager<Plugin>(this, this.pluginInterface);
        }

        private IntPtr SelectTabTargetIgnoreDepthDetour(IntPtr targetSystem, IntPtr camera, IntPtr gameObjects, bool inverse,
            IntPtr a5)
        {
            if (!Enable)
            {
                return tabIgnoreDepthHook.Original(targetSystem, camera, gameObjects, inverse, a5);
            }

            return SelectCustomTabTarget(targetSystem, camera, gameObjects, inverse, a5);
        }
        
        private IntPtr SelectTabTargetConeDetour(IntPtr targetSystem, IntPtr camera, IntPtr gameObjects, bool inverse,
            IntPtr a5)
        {
            if (!Enable)
            {
                return tabConeHook.Original(targetSystem, camera, gameObjects, inverse, a5);
            }
            
            return SelectCustomTabTarget(targetSystem, camera, gameObjects, inverse, a5);
        }

        private IntPtr SelectInitialTabTargetDetour(IntPtr targetSystem, IntPtr gameObjects, IntPtr camera, IntPtr a4)
        {
            if (!Enable)
            {
                return selectInitialTabTargetHook.Original(targetSystem, gameObjects, camera, a4);
            }

            return SelectCustomTabTarget(targetSystem, camera, gameObjects, false, new IntPtr(1));
        }
        
        private IntPtr SelectCustomTabTarget(IntPtr targetSystem, IntPtr camera, IntPtr gameObjects, bool inverse, IntPtr a5)
        {
            #if DEBUG
            PluginLog.Log($"SelectCustomTabTarget - targetSystem: {targetSystem.ToInt64():X}, gameObjects: {gameObjects.ToInt64():X}, camera: {camera.ToInt64():X}");
            var goCount = Marshal.ReadInt64(gameObjects);
            PluginLog.Log($"GameObject count: {goCount}");

            for (int i = 0; i < goCount; i++)
            {
                var val = Marshal.ReadIntPtr(gameObjects + (8 * (i + 1)));
                PluginLog.Log($"Obj index {i}: {val.ToInt64():X}");
            }
            #endif
            
            return tabIgnoreDepthHook.Original(targetSystem, camera, gameObjects, inverse, a5);
        }

        private int TargetSortComparatorDetour(IntPtr a1, IntPtr a2)
        {
            if (Mode == 1 || Mode == 2)
            {
                var actorId1 = Marshal.ReadIntPtr(a1 + 16);
                var actorId2 = Marshal.ReadIntPtr(a2 + 16);
                var origResult = targetSortComparatorHook.Original(a1, a2);

                var actorCurHp1 = Marshal.ReadInt32(actorId1 + ActorOffsets.CurrentHp);
                var actorMaxHp1 = Marshal.ReadInt32(actorId1 + ActorOffsets.MaxHp);
                var actor1Hp = (float) actorCurHp1 / actorMaxHp1;

                var actorCurHp2 = Marshal.ReadInt32(actorId2 + ActorOffsets.CurrentHp);
                var actorMaxHp2 = Marshal.ReadInt32(actorId2 + ActorOffsets.MaxHp);
                var actor2Hp = (float) actorCurHp2 / actorMaxHp2;

                #if DEBUG
                PluginLog.Log(
                    $"TargetSortComparator: comparing a1 {a1.ToInt64():X} {actorId1.ToInt64():X} to a2 {a2.ToInt64():X} {actorId2.ToInt64():X} = result of {origResult:X}");
                PluginLog.Log($"Actor 1 ({actorId1.ToInt64():X}: {actorCurHp1} / {actorMaxHp1} HP - {actor1Hp}");
                PluginLog.Log($"Actor 2 ({actorId2.ToInt64():X}: {actorCurHp2} / {actorMaxHp2} HP - {actor2Hp}");
                #endif

                if (actor1Hp > actor2Hp)
                {
                    return Mode == 1 ? 1 : -1;
                }
                else if (actor1Hp < actor2Hp)
                {
                    return Mode == 1 ? -1 : 1;
                }
                else
                {
                    return origResult;
                }
            }

            return targetSortComparatorHook.Original(a1, a2);       
        }
        
        private IntPtr OnTabTargetDetour(IntPtr a1, IntPtr a2, IntPtr a3, IntPtr a4)
        {
            #if DEBUG
            PluginLog.Log($"TabTarget - a1: {a1.ToInt64():X}, a2: {a2.ToInt64():X}, a3: {a3.ToInt64():X}, a4: {a4.ToInt64():X}");
            #endif
            
            return tabTargetHook.Original(a1, a2, a3, a4);
        }
        
        [Command("/ptab")]
        [HelpMessage("Opens the TabTab plugin configuration.")]
        public void CommandOpenConfig(string command, string args)
        {
            this.ui.IsVisible = true;
        }

        #region IDisposable Support
        protected virtual void Dispose(bool disposing)
        {
            if (!disposing) return;

            this.tabIgnoreDepthHook.Disable();
            this.tabConeHook.Disable();
            this.selectInitialTabTargetHook.Disable();
            this.targetSortComparatorHook.Disable();
            this.tabTargetHook.Disable();
            
            this.tabIgnoreDepthHook.Dispose();
            this.tabConeHook.Dispose();
            this.selectInitialTabTargetHook.Dispose();
            this.targetSortComparatorHook.Dispose();
            this.tabTargetHook.Dispose();
            
            this.commandManager.Dispose();

            // Write transient config values
            this.config.Enabled = this.Enable;
            this.config.Mode = this.Mode;
            
            this.pluginInterface.SavePluginConfig(this.config);

            this.pluginInterface.UiBuilder.OnBuildUi -= this.ui.Draw;

            this.pluginInterface.Dispose();
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        #endregion
    }
}
