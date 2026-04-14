using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;

namespace RxFSM
{
    internal static class FSMLoop
    {
        internal const int STAGE_TIMERS   = 0;
        internal const int STAGE_TRIGGERS = 1;
        internal const int STAGE_TICKS    = 2;

        private static readonly List<Action<float>>[] _stages =
        {
            new List<Action<float>>(),
            new List<Action<float>>(),
            new List<Action<float>>()
        };

        private struct FSMLoopMarker { }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Init()
        {
            for (int i = 0; i < _stages.Length; i++)
                _stages[i].Clear();

            var root = PlayerLoop.GetCurrentPlayerLoop();
            if (!IsInserted(root))
            {
                InsertIntoUpdate(ref root);
                PlayerLoop.SetPlayerLoop(root);
            }
        }

        static bool IsInserted(PlayerLoopSystem sys)
        {
            if (sys.type == typeof(FSMLoopMarker)) return true;
            if (sys.subSystemList == null) return false;
            foreach (var sub in sys.subSystemList)
                if (IsInserted(sub)) return true;
            return false;
        }

        static bool InsertIntoUpdate(ref PlayerLoopSystem sys)
        {
            if (sys.subSystemList == null) return false;

            for (int i = 0; i < sys.subSystemList.Length; i++)
            {
                if (sys.subSystemList[i].type == typeof(Update))
                {
                    var subs = new List<PlayerLoopSystem>(
                        sys.subSystemList[i].subSystemList ?? Array.Empty<PlayerLoopSystem>());

                    int idx = subs.FindIndex(s => s.type == typeof(Update.ScriptRunBehaviourUpdate));
                    subs.Insert(idx >= 0 ? idx + 1 : subs.Count, new PlayerLoopSystem
                    {
                        type = typeof(FSMLoopMarker),
                        updateDelegate = Tick
                    });
                    sys.subSystemList[i].subSystemList = subs.ToArray();
                    return true;
                }
            }

            for (int i = 0; i < sys.subSystemList.Length; i++)
                if (InsertIntoUpdate(ref sys.subSystemList[i]))
                    return true;

            return false;
        }

        static void Tick()
        {
            float dt = Time.deltaTime;
            for (int s = 0; s < _stages.Length; s++)
            {
                var snap = _stages[s].ToArray();
                foreach (var cb in snap) cb(dt);
            }
        }

        internal static IDisposable Register(int stage, Action<float> callback)
        {
            _stages[stage].Add(callback);
            return Disposable.Create(() => _stages[stage].Remove(callback));
        }

        internal static IDisposable Register(int stage, Action callback)
        {
            Action<float> wrapped = _ => callback();
            return Register(stage, wrapped);
        }
    }
}
