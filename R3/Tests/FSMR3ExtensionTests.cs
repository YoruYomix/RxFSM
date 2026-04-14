using System.Collections;
using UnityEngine;
using R3;

namespace RxFSM
{
    public class FSMR3ExtensionTests : MonoBehaviour
    {
        enum TestState { Idle, Walk, Run, Hit }
        readonly struct Damaged { public readonly float amount; public Damaged(float a) { amount = a; } }

        int _pass, _fail;
        void Assert(bool c, string label)
        {
            if (c) { Debug.Log($"[PASS] {label}"); _pass++; }
            else   { Debug.LogError($"[FAIL] {label}"); _fail++; }
        }

        void Start() => StartCoroutine(RunAll());

        IEnumerator RunAll()
        {
            yield return T_R3Connect();
            PrintFinal();
        }

        void PrintFinal()
        {
            int total = _pass + _fail;
            if (_fail == 0) Debug.Log($"=== FSMR3ExtensionTests: {_pass}/{total} passed ===");
            else            Debug.LogError($"=== FSMR3ExtensionTests: {_pass}/{total} passed, {_fail} FAILED ===");
        }

        // ── Observable.Connect ────────────────────────────────────────────────────

        IEnumerator T_R3Connect()
        {
            var sm = FSM.Create<TestState>(TestState.Idle)
                .AddTransition<Damaged>(TestState.Idle, TestState.Hit)
                .Build();

            var subject = new Subject<Damaged>();
            var sub = subject.Connect(sm);

            subject.OnNext(new Damaged(10f));
            Assert(sm.State == TestState.Hit, "IObservable.Connect routes OnNext to FSM");

            sub.Dispose();
            sm.Dispose();
            yield return null;
        }
    }
}