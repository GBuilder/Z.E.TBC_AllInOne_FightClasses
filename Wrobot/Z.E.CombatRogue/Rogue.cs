﻿using System;
using System.Diagnostics;
using System.Threading;
using robotManager.Helpful;
using robotManager.Products;
using wManager.Events;
using wManager.Wow.Class;
using wManager.Wow.Helpers;
using wManager.Wow.ObjectManager;
using System.Collections.Generic;
using wManager.Wow.Bot.Tasks;
using System.ComponentModel;

public static class Rogue
{
    private static float _meleRange = Main.settingRange;
    private static float _pullRange = 25f;
    internal static Stopwatch _pullMeleeTimer = new Stopwatch();
    internal static Stopwatch _meleeTimer = new Stopwatch();
    internal static Stopwatch _stealthApproachTimer = new Stopwatch();
    private static WoWLocalPlayer Me = ObjectManager.Me;
    internal static ZERogueSettings _settings;
    private static bool _fightingACaster = false;
    private static List<string> _casterEnemies = new List<string>();
    private static bool _pullFromAfar = false;
    private static bool _isStealthApproching;

    public static void Initialize()
    {
        Main.Log("Initialized");
        ZERogueSettings.Load();
        _settings = ZERogueSettings.CurrentSetting;

        // Fight End
        FightEvents.OnFightEnd += (ulong guid) =>
        {
            _meleeTimer.Reset();
            _pullMeleeTimer.Reset();
            _stealthApproachTimer.Reset();
            _fightingACaster = false;
            _pullFromAfar = false;
            _isStealthApproching = false;
            Main.settingRange = _meleRange;
        };

        // We override movement to target when approaching in Stealth
        MovementEvents.OnMoveToPulse += (Vector3 point, CancelEventArgs cancelable) =>
        {
            if (_isStealthApproching &&
            !point.ToString().Equals(ToolBox.BackofVector3(ObjectManager.Target.Position, ObjectManager.Target, 2.5f).ToString()))
                cancelable.Cancel = true;
        };

        // Fight Loop - Go behind target when gouged
        FightEvents.OnFightLoop += (WoWUnit unit, CancelEventArgs cancelable) =>
        {
            if ((ObjectManager.Target.HaveBuff("Gouge"))
            && !MovementManager.InMovement && Me.IsAlive && !Me.IsCast)
            {
                if (Me.IsAlive && ObjectManager.Target.IsAlive)
                {
                    Vector3 position = ToolBox.BackofVector3(ObjectManager.Target.Position, ObjectManager.Target, 2.5f);
                    MovementManager.Go(PathFinder.FindPath(position), false);

                    while (MovementManager.InMovement && Conditions.InGameAndConnectedAndAliveAndProductStartedNotInPause
                    && (ObjectManager.Target.HaveBuff("Gouge")))
                    {
                        // Wait follow path
                        Thread.Sleep(500);
                    }
                }
            }
        };

        // BL Hook
        OthersEvents.OnAddBlackListGuid += (ulong guid, int timeInMilisec, bool isSessionBlacklist, CancelEventArgs cancelable) =>
        {
            Main.Log("BL : " + guid + " ms : " + timeInMilisec + " is session: " + isSessionBlacklist);
            cancelable.Cancel = true;
        };

        Rotation();
    }

    public static void Dispose()
    {
        Main.Log("Stop in progress.");
    }

    internal static void Rotation()
	{
        Main.Log("Started");
		while (Main._isLaunched)
		{
			try
			{
				if (!Products.InPause && !ObjectManager.Me.IsDeadMe)
                {
                    // Buff rotation
                    if (!Fight.InFight && ObjectManager.GetNumberAttackPlayer() < 1)
                    {
                        BuffRotation();
                    }

                    // Pull & Combat rotation
                    if (Fight.InFight && ObjectManager.Me.Target > 0UL && ObjectManager.Target.IsAttackable 
                        && ObjectManager.Target.IsAlive)
                    {
                        if (ObjectManager.GetNumberAttackPlayer() < 1 && !ObjectManager.Target.InCombatFlagOnly)
                            Pull();
                        else
                            CombatRotation();
                    }
                }
			}
			catch (Exception arg)
			{
				Logging.WriteError("ERROR: " + arg, true);
			}
			Thread.Sleep(Usefuls.Latency + 10);
		}
        Main.Log("Stopped.");
    }

    internal static void BuffRotation()
    {
        if (!Me.IsMounted && !Me.IsCast)
        {

        }
    }

    internal static void Pull()
    {
        // Check if surrounding enemies
        if (ObjectManager.Target.GetDistance < _pullRange && !_pullFromAfar)
            _pullFromAfar = ToolBox.CheckIfEnemiesOnPull(ObjectManager.Target, _pullRange);

        // Pull from afar
        if ((_pullFromAfar && _pullMeleeTimer.ElapsedMilliseconds < 5000) || _settings.AlwaysPull
            && ObjectManager.Target.GetDistance <= _pullRange)
        {
            Spell pullMethod = null;

            if (Shoot.IsSpellUsable && Shoot.KnownSpell)
                pullMethod = Shoot;

            if (Throw.IsSpellUsable && Throw.KnownSpell)
                pullMethod = Throw;

            if (pullMethod == null)
            {
                Main.Log("Can't pull from distance. Please equip a ranged weapon in order to Throw or Shoot.");
                _pullFromAfar = false;
            }
            else
            {
                if (Me.IsMounted)
                    MountTask.DismountMount();

                Main.settingRange = _pullRange;
                if (Cast(pullMethod))
                    Thread.Sleep(2000);
            }
        }

        // Melee ?
        if (_pullMeleeTimer.ElapsedMilliseconds <= 0 && ObjectManager.Target.GetDistance <= _pullRange + 3)
            _pullMeleeTimer.Start();

        if (_pullMeleeTimer.ElapsedMilliseconds > 5000)
        {
            Main.LogDebug("Going in Melee range");
            Main.settingRange = _meleRange;
            _pullMeleeTimer.Reset();
        }

        // Check if caster in list
        if (_casterEnemies.Contains(ObjectManager.Target.Name))
            _fightingACaster = true;

        // Stealth
        if (!Me.HaveBuff("Stealth") && !_pullFromAfar && ObjectManager.Target.GetDistance > 15f 
            /*&& ObjectManager.Target.GetDistance < 25f*/ && _settings.StealthApproach && Backstab.KnownSpell)
            if (Cast(Stealth))
                return;

        // Un-Stealth
        if (Me.HaveBuff("Stealth") && _pullFromAfar && ObjectManager.Target.GetDistance > 15f)
            if (Cast(Stealth))
                return;

        // Stealth approach
        if (Me.HaveBuff("Stealth") && ObjectManager.Target.GetDistance > 3f && !_isStealthApproching && !_pullFromAfar)
        {
            Main.settingRange = _meleRange;
            _stealthApproachTimer.Start();
            _isStealthApproching = true;
            if (ObjectManager.Me.IsAlive && ObjectManager.Target.IsAlive)
            {

                while (Conditions.InGameAndConnectedAndAliveAndProductStartedNotInPause
                && (ObjectManager.Target.GetDistance > 4f)
                && !ToolBox.CheckIfEnemiesOnPull(ObjectManager.Target, _pullRange) && Fight.InFight
                && _stealthApproachTimer.ElapsedMilliseconds <= 25000 && Me.HaveBuff("Stealth"))
                {
                    // deactivate autoattack
                    ToggleAutoAttack(false);

                    Vector3 position = ToolBox.BackofVector3(ObjectManager.Target.Position, ObjectManager.Target, 2.5f);
                    MovementManager.MoveTo(position);
                    // Wait follow path
                    Thread.Sleep(50);
                }

                if (ToolBox.CheckIfEnemiesOnPull(ObjectManager.Target, _pullRange) && Me.HaveBuff("Stealth"))
                {
                    _pullFromAfar = true;
                    if (Cast(Stealth))
                        return;
                }
                
                // Opener
                if (ToolBox.MeBehindTarget())
                {
                    if (Cast(Backstab))
                        MovementManager.StopMove();
                }
                else
                {
                    if (Cast(Gouge))
                        MovementManager.StopMove();
                }

                if (_stealthApproachTimer.ElapsedMilliseconds > 25000)
                {
                    Main.Log("_stealthApproachTimer time out");
                    _pullFromAfar = true;
                }

                ToggleAutoAttack(true);
                _isStealthApproching = false;
            }
        }

        // Auto
        if (ObjectManager.Target.GetDistance < 6f && !Me.HaveBuff("Stealth"))
            ToggleAutoAttack(true);
    }

    internal static void CombatRotation()
    {
        bool _shouldBeInterrupted = ToolBox.EnemyCasting();
        bool _inMeleeRange = ObjectManager.Target.GetDistance < 6f;
        WoWUnit _target = ObjectManager.Target;

        // Check Auto-Attacking
        ToggleAutoAttack(true);

        // Check if interruptable enemy is in list
        if (_shouldBeInterrupted)
        {
            _fightingACaster = true;
            if (!_casterEnemies.Contains(ObjectManager.Target.Name))
                _casterEnemies.Add(ObjectManager.Target.Name);
        }

        // Melee ?
        if (_pullMeleeTimer.ElapsedMilliseconds > 0)
            _pullMeleeTimer.Reset();

        if (_meleeTimer.ElapsedMilliseconds <= 0 && _pullFromAfar)
            _meleeTimer.Start();

        if ((_shouldBeInterrupted || _meleeTimer.ElapsedMilliseconds > 5000) && Main.settingRange != _meleRange)
        {
            Main.LogDebug("Going in Melee range 2");
            Main.settingRange = _meleRange;
            _meleeTimer.Stop();
        }

        // Evasion
        if (Me.HealthPercent < 30 && !Me.HaveBuff("Evasion"))
            if (Cast(Evasion))
                return;

        // Backstab in combat
        if (_target.HaveBuff("Gouge"))
            if (Cast(Backstab))
                return;

        // Eviscerate logic
        if ((Me.ComboPoint > 0 && _target.HealthPercent < 30)
            || (Me.ComboPoint > 1 && _target.HealthPercent < 45)
            || (Me.ComboPoint > 2 && _target.HealthPercent < 60)
            || (Me.ComboPoint > 3 && _target.HealthPercent < 70))
            if (Cast(Eviscerate))
                return;

        // Sinister Strike
        if (Me.ComboPoint < 5 && !_target.HaveBuff("Gouge"))
            if (Cast(SinisterStrike))
                return;
    }

    public static void ShowConfiguration()
    {
        ZERogueSettings.Load();
        ZERogueSettings.CurrentSetting.ToForm();
        ZERogueSettings.CurrentSetting.Save();
    }

    private static Spell Attack = new Spell("Attack");
    private static Spell Shoot = new Spell("Shoot");
    private static Spell Throw = new Spell("Throw");
    private static Spell Eviscerate = new Spell("Eviscerate");
    private static Spell SinisterStrike = new Spell("Sinister Strike");
    private static Spell Stealth = new Spell("Stealth");
    private static Spell Backstab = new Spell("Backstab");
    private static Spell Gouge = new Spell("Gouge");
    private static Spell Evasion = new Spell("Evasion");

    internal static bool Cast(Spell s)
    {
        if (!s.KnownSpell)
            return false;

        Main.LogDebug("In cast for " + s.Name);
        if (!s.IsSpellUsable || Me.IsCast)
            return false;
        
        s.Launch();
        return true;
    }

    private static void ToggleAutoAttack(bool activate)
    {
        bool _autoAttacking = Lua.LuaDoString<bool>("isAutoRepeat = false; if IsCurrentSpell('Attack') " +
            "then isAutoRepeat = true end", "isAutoRepeat");

        if (!_autoAttacking && activate && !ObjectManager.Target.HaveBuff("Gouge"))
        {
            Main.Log("Turning auto attack ON");
            ToolBox.CheckAutoAttack(Attack);
        }

        if (!activate && _autoAttacking)
        {
            Main.Log("Turning auto attack OFF");
            Attack.Launch();
        }
    }
}
