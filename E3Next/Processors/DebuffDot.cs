﻿using E3Core.Classes;
using E3Core.Data;
using E3Core.Settings;
using E3Core.Utility;
using MonoCore;
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;

namespace E3Core.Processors
{
    public static class DebuffDot
    {
        public static Logging _log = E3._log;
        private static IMQ MQ = E3.MQ;
        private static ISpawns _spawns = E3._spawns;
        public static Dictionary<Int32, SpellTimer> _debuffTimers = new Dictionary<Int32, SpellTimer>();
        public static Dictionary<Int32, SpellTimer> _dotTimers = new Dictionary<Int32, SpellTimer>();
        public static Dictionary<Int32, SpellTimer> _OffAssistTimers = new Dictionary<Int32, SpellTimer>();

        public static HashSet<Int32> _mobsToDot = new HashSet<int>();
        public static HashSet<Int32> _mobsToDebuff = new HashSet<int>();
        public static HashSet<Int32> _mobsToOffAsist = new HashSet<int>();
        public static HashSet<Int32> _mobsToIgnoreOffAsist = new HashSet<int>();
        public static List<Int32> _deadMobs = new List<int>();
        
        private static Int64 _nextDebuffCheck = 0;
        private static Int64 _nextDebuffCheckInterval = 1000;
        private static Int64 _nextDoTCheck = 0;
        private static Int64 _nextDoTCheckInterval = 1000;
        private static Int64 _nextOffAssistCheck = 0;
        private static Int64 _nextOffAssistCheckInterval = 500;
        private static bool _shouldOffAssist = true;
        private static List<Data.Spell> _tempOffAssistSpellList = new List<Spell>();
        public static void Init()
        {
            RegisterEvents();
        }
        public static void Reset()
        {
            _mobsToDot.Clear();
            _mobsToDebuff.Clear();
            _mobsToOffAsist.Clear();
            _mobsToIgnoreOffAsist.Clear();
            foreach (var kvp in _debuffTimers)
            {
                kvp.Value.Dispose();
            }
            _debuffTimers.Clear();
            foreach (var kvp in _dotTimers)
            {
                kvp.Value.Dispose();
            }
            _dotTimers.Clear();

           
        }
        [AdvSettingInvoke]
        public static void Check_OffAssistSpells()
        {
            //TODO: Test
            if (!_shouldOffAssist) return;
            if (!Assist._isAssisting) return;
            if (E3._characterSettings.OffAssistSpells.Count == 0) return;
            if (!e3util.ShouldCheck(ref _nextOffAssistCheck, _nextOffAssistCheckInterval)) return;

            //check xtargets
            for (Int32 i=1;i<=13;i++)
            {
                bool autoHater = MQ.Query<bool>($"${{Me.XTarget[{i}].TargetType.Equal[Auto Hater]}}");
                if (!autoHater) continue;
                Int32 mobId = MQ.Query<Int32>($"${{Me.XTarget[{i}].ID}}");
                if(mobId>0)
                {
                    if (_mobsToOffAsist.Contains(mobId) || _mobsToIgnoreOffAsist.Contains(mobId)) continue;
                    Spawn s;
                    if(_spawns.TryByID(mobId,out s))
                    {
                        if (s.ID == Assist._assistTargetID) continue;
                        if (!s.Targetable) continue;
                        if (!s.Aggressive) continue;
                        if (!s.Targetable) continue;
                        if (s.PctHps < 10) continue;
                        if (!MQ.Query<bool>($"${{Spawn[npc id {mobId}].LineOfSight}}")) continue;
                        if (s.Distance > 100) continue;
                        if (s.TypeDesc == "Corpse") continue;
                        if (s.Name.Contains("'s pet'")) continue;
                        if (s.Name.IndexOf("Chest", StringComparison.OrdinalIgnoreCase) > -1) continue;
                        if (s.Name.IndexOf("a box", StringComparison.OrdinalIgnoreCase) > -1) continue;
                        if (s.Name.IndexOf("crate", StringComparison.OrdinalIgnoreCase) > -1) continue;
                        if (s.Name.IndexOf("hollow_tree", StringComparison.OrdinalIgnoreCase) > -1) continue;
                        if (s.Name.IndexOf("wooden box", StringComparison.OrdinalIgnoreCase) > -1) continue;
                        _mobsToOffAsist.Add(mobId);
                    }
                }
            }

            if (_mobsToOffAsist.Count == 0) return;
            //lets place the 1st offensive spell on each mob, then the next, then the next
            foreach (var spell in E3._characterSettings.OffAssistSpells)
            {
                if(Casting.CheckMana(spell))
                {
                    _tempOffAssistSpellList.Clear();
                    _tempOffAssistSpellList.Add(spell);
                    foreach (Int32 mobid in _mobsToOffAsist.ToList())
                    {
                        CastLongTermSpell(mobid, _tempOffAssistSpellList, _OffAssistTimers);
                        if (E3._actionTaken) return;
                    }
                }
                
            }
  
        }

        [AdvSettingInvoke]
        public static void Check_Debuffs()
        {

            if (Assist._assistTargetID > 0)
            {
                CastLongTermSpell(Assist._assistTargetID, E3._characterSettings.Debuffs_OnAssist, _debuffTimers);
                if (E3._actionTaken) return;
            }

            if (!e3util.ShouldCheck(ref _nextDebuffCheck, _nextDebuffCheckInterval)) return;

            //e3util.PrintTimerStatus(_debuffTimers, ref _nextDebuffCheck, "Debuffs");

            foreach (var mobid in _mobsToDebuff)
            {

                CastLongTermSpell(mobid, E3._characterSettings.Debuffs_Command, _debuffTimers);
                if (E3._actionTaken) return;
            }
            foreach (var mobid in _deadMobs)
            {
                _mobsToDot.Remove(mobid);
                _mobsToDebuff.Remove(mobid);
            }
            if (_deadMobs.Count > 0) _deadMobs.Clear();

            //put us back to our assist target
            Int32 targetId = MQ.Query<Int32>("${Target.ID}");
            if (targetId != Assist._assistTargetID)
            {
                Casting.TrueTarget(Assist._assistTargetID);

            }

        }
        [AdvSettingInvoke]
        public static void check_Dots()
        {

            if (Assist._assistTargetID > 0)
            {
                CastLongTermSpell(Assist._assistTargetID, E3._characterSettings.Dots_Assist, _dotTimers);
                if (E3._actionTaken) return;
            }


            if (!e3util.ShouldCheck(ref _nextDoTCheck, _nextDoTCheckInterval)) return;
           // e3util.PrintTimerStatus(_dotTimers, ref _nextDoTCheck, "Damage over Time");

            foreach (var mobid in _mobsToDot)
            {
                CastLongTermSpell(mobid, E3._characterSettings.Dots_OnCommand, _dotTimers);
                if (E3._actionTaken) return;
            }
            foreach (var mobid in _deadMobs)
            {
                _mobsToDot.Remove(mobid);
                _mobsToDebuff.Remove(mobid);
            }
            if (_deadMobs.Count > 0) _deadMobs.Clear();

            //put us back to our assist target
            Int32 targetId = MQ.Query<Int32>("${Target.ID}");
            if (targetId != Assist._assistTargetID)
            {
                Casting.TrueTarget(Assist._assistTargetID);

            }
        }
        public static void DotsOn(Int32 mobid)
        {
            if (!_mobsToDot.Contains(mobid))
            {
                _mobsToDot.Add(mobid);
            }
        }
        private static void RegisterEvents()
        {

            e3util.RegisterCommandWithTarget("/dotson", DotsOn);
            e3util.RegisterCommandWithTarget("/dot", DotsOn);
            EventProcessor.RegisterCommand("/debuffsoff", (x) =>
            {
                _mobsToDebuff.Clear();
                if (x.args.Count == 0)
                {
                    //we are telling people to back off
                    E3._bots.BroadcastCommandToGroup($"/debuffsoff all");
                }

            });
            EventProcessor.RegisterCommand("/dotsoff", (x) =>
            {
                _mobsToDot.Clear();
                if (x.args.Count == 0)
                {
                    //we are telling people to back off
                    E3._bots.BroadcastCommandToGroup($"/dotsoff all");
                }

            });
            e3util.RegisterCommandWithTarget("/debuffson", DebuffsOn);
            e3util.RegisterCommandWithTarget("/debuff", DebuffsOn);

            EventProcessor.RegisterCommand("/offassiston", (x) =>
            {
                if (x.args.Count == 0)
                {
                    _shouldOffAssist = true;
                    E3._bots.BroadcastCommandToGroup("/offassiston all");
                }
                else
                {
                    //we are turning our own loot on.
                    _shouldOffAssist = true;
                    E3._bots.Broadcast("\a#336699Turning on OffAssist.");
                }
            });
            EventProcessor.RegisterCommand("/offassistoff", (x) =>
            {
                if (x.args.Count == 0)
                {
                    _shouldOffAssist = false;
                    E3._bots.BroadcastCommandToGroup("/offassistoff all");
                }
                else
                {
                    //we are turning our own loot on.
                    _shouldOffAssist = false;
                    E3._bots.Broadcast("\a-gTurning Off OffAssist.");
                }
            });

            EventProcessor.RegisterCommand("/offassistignore", (x) =>
            {
                if (x.args.Count ==3)
                {
                    string command = x.args[1].ToLower();
                    Int32 targetid;
                    if (Int32.TryParse(x.args[2], out targetid))
                    {
                        if (command == "add")
                        {
                            E3._bots.Broadcast($"Trying to add {targetid} to the off assist ignore list.");
                            if(!_mobsToIgnoreOffAsist.Contains(targetid))
                            {
                                _mobsToIgnoreOffAsist.Add(targetid);
                            }
                        }
                        else if (command == "remove")
                        {
                            E3._bots.Broadcast($"Removing {targetid} from the off assist ignore list.");

                            _mobsToIgnoreOffAsist.Remove(targetid);
                        }
                    }
                } 
                else if(x.args.Count==2)
                {
                    string command = x.args[0].ToLower();
                    Int32 targetid;
                    if (Int32.TryParse(x.args[1], out targetid))
                    {
                        if (command == "add")
                        {
                            if (!_mobsToIgnoreOffAsist.Contains(targetid))
                            {
                                _mobsToIgnoreOffAsist.Add(targetid);
                            }
                            E3._bots.BroadcastCommandToGroup($"/offassistignore all {command} {targetid}");
                        }
                        else if (command == "remove")
                        {
                            _mobsToIgnoreOffAsist.Remove(targetid);
                            E3._bots.BroadcastCommandToGroup($"/offassistignore all {command} {targetid}");
                        }
                    }
                }
            });

        }
        public static void DebuffsOn(Int32 mobid)
        {
            if (!_mobsToDebuff.Contains(mobid))
            {
                _mobsToDebuff.Add(mobid);
            }
        }
        private static void CastLongTermSpell(Int32 mobid, List<Data.Spell> spells, Dictionary<Int32, SpellTimer> timers)
        {

            foreach (var spell in spells)
            {
                //do we already have a timer on this spell?
                SpellTimer s;
                if (timers.TryGetValue(mobid, out s))
                {
                    Int64 timestamp;
                    if (s._timestamps.TryGetValue(spell.SpellID, out timestamp))
                    {
                        if (Core._stopWatch.ElapsedMilliseconds < timestamp)
                        {
                            //debuff/dot is still on the mob, kick off
                            continue;
                        }
                    }
                }
                ResistCounter r;
                if (Casting._resistCounters.TryGetValue(mobid, out r))
                {
                    //have resist counters on this mob, lets check if this spell is on the list
                    Int32 counters;
                    if (r._spellCounters.TryGetValue(spell.SpellID, out counters))
                    {
                        if (counters > spell.MaxTries)
                        {   //mob is resistant to this spell, kick out. 
                            continue;
                        }
                    }
                }
                if (Casting.InRange(mobid, spell) && Casting.CheckReady(spell) && Casting.CheckMana(spell))
                {

                    //lets make sure the buffs/debuffs are there
                    if (!Casting.TrueTarget(mobid))
                    {
                        //can't target it, so kick out for this mob
                        _deadMobs.Add(mobid);
                        return;
                    }
                    if (MQ.Query<bool>($"${{Bool[${{Spawn[id {mobid}].Type.Equal[Corpse]}}]}}"))
                    {
                        _deadMobs.Add(mobid);
                        return;
                        //its dead jim, leave it be

                    }
                    MQ.Delay(2000, "${Target.BuffsPopulated}");
                    //check if the if condition works
                    if (!String.IsNullOrWhiteSpace(spell.Ifs))
                    {
                        if (!MQ.Query<bool>($"${{If[{spell.Ifs},TRUE,FALSE]}}"))
                        {
                            continue;
                        }
                    }
                    if (!String.IsNullOrWhiteSpace(spell.CastIF))
                    {
                        if (!MQ.Query<bool>($"${{Bool[${{Target.Buff[{spell.CastIF}]}}]}}"))
                        {
                            //doesn't have the buff we want
                            continue;
                        }
                    }
                    if (!String.IsNullOrWhiteSpace(spell.CheckFor))
                    {
                        if (MQ.Query<bool>($"${{Bool[${{Target.Buff[{spell.CheckFor}]}}]}}"))
                        {
                            //has the buff already
                            //lets set the timer for it so we dont' have to keep targeting it.
                            Int64 buffDuration = MQ.Query<Int64>($"${{Target.BuffDuration[{spell.CheckFor}]}}");
                            if (buffDuration < 1000)
                            {
                                buffDuration = 1000;
                            }
                            UpdateDotDebuffTimers(mobid, spell, buffDuration, timers);
                            continue;
                        }
                    }
                    var result = Casting.Cast(mobid, spell, Heals.SomeoneNeedsHealing);
                    if (result == CastReturn.CAST_INTERRUPTFORHEAL)
                    {
                        return;
                    }
                    ////Okay lesson about EQ resist messages and timers for debuffs/buffs
                    //// you don't know if a spell resits unless the server tells you.. so this result above? somewhat unreliable.
                    //// The reason for this is, it takes X amount of time to come back from the server , and that X is unreliable as heck. 
                    //// So... for debuffs we are going to do this. If the target you have has the buff, grab its timer from the buffs object
                    //// for total time as as the Duration TLO can be unreliable depending on dot focus duration.  as in it says 72  sec when its 92 sec.
                    Casting.TrueTarget(mobid);

                    MQ.Delay(2000, "${Target.BuffsPopulated}");
                    //// we also have the situation where over 55> buffs on the ROF2 client cannot be viewed, but up to 85 or so work. 
                    //// we are going to have to loop through the buffs and set dot timers
                    //// if under 55< we will evict off the timer that we think we should have if we do
                    //// if over 55> we will update but not evict... best we can do. so if a dot goes over the 55 buff cap
                    //// but we get an invalid resist message... well... the client is going to assume it landed and set a timer for it. 
                    //// Most of the time this won't happen, but sometimes.. well.. ya. not much I can do.

                    //delay to release back to MQ to get a proper buffcount
                    MQ.Delay(100);
                    Int32 buffCount = MQ.Query<Int32>("${Target.BuffCount}");
                    //lets just update our cache with what is on the mob.
                    Int64 timeLeftInMS = Casting.TimeLeftOnMySpell(spell);
                    if (buffCount < 55)
                    {
                        UpdateDotDebuffTimers(mobid, spell, timeLeftInMS, timers);
                    }
                    else
                    {
                        Int64 totalTimeToWait;
                        if (timeLeftInMS > 0)
                        {
                            totalTimeToWait = timeLeftInMS;
                        }
                        else
                        {
                            if (result == CastReturn.CAST_INTERRUPTED || result == CastReturn.CAST_INTERRUPTFORHEAL || result == CastReturn.CAST_FIZZLE)
                            {
                                return;
                            }
                            if (result != CastReturn.CAST_SUCCESS)
                            {
                                //zero it out
                                totalTimeToWait = 0;
                            }
                            else
                            {
                                totalTimeToWait = (spell.DurationTotalSeconds * 1000);
                            }
                        }
                        UpdateDotDebuffTimers(mobid, spell, totalTimeToWait, timers);
                    }
                    //onto the next debuff/dot!
                    if (E3._actionTaken) return;

                }
            }
        }
        private static void UpdateDotDebuffTimers(Int32 mobid, Data.Spell spell, Int64 timeLeftInMS, Dictionary<Int32, SpellTimer> timers)
        {
            SpellTimer s;
            //if we have no time left, as it was not found, just set it to 0 in ours
            if (timers.TryGetValue(mobid, out s))
            {
                if (!s._timestamps.ContainsKey(spell.SpellID))
                {
                    s._timestamps.Add(spell.SpellID, 0);
                }

                s._timestamps[spell.SpellID] = Core._stopWatch.ElapsedMilliseconds + timeLeftInMS;

            }
            else
            {
                SpellTimer ts = SpellTimer.Aquire();
                ts._mobID = mobid;

                ts._timestamps.Add(spell.SpellID, Core._stopWatch.ElapsedMilliseconds + timeLeftInMS);
                timers.Add(mobid, ts);
            }
        }
       
       
    }

}
