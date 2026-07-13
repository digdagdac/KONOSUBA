using System;
using UnityEngine;

namespace Overbless.Runtime
{
    public enum ExitGateState
    {
        Locked,
        Open,
        Entered
    }

    [DisallowMultipleComponent]
    public sealed class ExitGate : MonoBehaviour
    {
        [SerializeField] private Collider2D entryTrigger;

        public event Action Opened;
        public event Action Entered;

        public ExitGateState State { get; private set; } = ExitGateState.Locked;
        public bool IsLocked => State == ExitGateState.Locked;
        public bool IsOpen => State == ExitGateState.Open;
        public bool IsEntered => State == ExitGateState.Entered;

        private void Awake()
        {
            ValidateConfiguration();
            ResetGate();
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            var playerLifeCycle = other.GetComponentInParent<PlayerLifeCycle>();
            if (playerLifeCycle == null)
            {
                return;
            }

            TryEnter(playerLifeCycle);
        }

        public bool Open()
        {
            if (State != ExitGateState.Locked)
            {
                return false;
            }

            State = ExitGateState.Open;
            NotifyObservers(Opened, "Exit-open");
            return true;
        }

        public bool TryEnter(PlayerLifeCycle playerLifeCycle)
        {
            if (playerLifeCycle == null)
            {
                throw new ArgumentNullException(nameof(playerLifeCycle));
            }

            if (State != ExitGateState.Open || !playerLifeCycle.IsAlive)
            {
                return false;
            }

            State = ExitGateState.Entered;
            NotifyObservers(Entered, "Exit-entry");
            return true;
        }

        public void ResetGate()
        {
            State = ExitGateState.Locked;
        }

        private static void NotifyObservers(Action observers, string operation)
        {
            if (observers == null)
            {
                return;
            }

            var errors = new System.Collections.Generic.List<Exception>();
            foreach (Action observer in observers.GetInvocationList())
            {
                try
                {
                    observer();
                }
                catch (Exception exception)
                {
                    errors.Add(exception);
                }
            }

            if (errors.Count == 1)
            {
                throw new InvalidOperationException(operation + " observer failed.", errors[0]);
            }

            if (errors.Count > 1)
            {
                throw new AggregateException(operation + " observers failed.", errors);
            }
        }
        private void ValidateConfiguration()
        {
            if (entryTrigger == null)
            {
                throw new InvalidOperationException("ExitGate requires an entry trigger.");
            }

            if (!entryTrigger.isTrigger)
            {
                throw new InvalidOperationException("ExitGate entry collider must be a trigger.");
            }
            if (entryTrigger.GetComponent<ExitGate>() != this)
            {
                throw new InvalidOperationException("ExitGate entry trigger must be on the ExitGate GameObject.");
            }
        }
    }
}
