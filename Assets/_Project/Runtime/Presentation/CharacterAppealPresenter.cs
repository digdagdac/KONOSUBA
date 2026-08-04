using System;
using UnityEngine;
using UnityEngine.UI;

namespace Overbless.Runtime
{
    /// <summary>
    /// Shows one cast member at the four approved moments: first encounter, blessing
    /// choice, victory and defeat. Nothing else opens the card, so the pixel combat
    /// view stays the authoritative read during play.
    /// </summary>
    /// <remarks>
    /// The card is presentation only. It observes existing runtime signals, never
    /// consumes input, and never writes to the identity catalog or to gameplay state.
    /// Timing uses unscaled time so a card raised while the blessing choice holds the
    /// game at zero time scale still resolves.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class CharacterAppealPresenter : MonoBehaviour
    {
        private static readonly Color NeutralTint = new Color(0.09f, 0.11f, 0.18f, 1f);
        private static readonly Color ConfidentTint = new Color(1f, 1f, 1f, 1f);
        private static readonly Color HurtTint = new Color(0.9f, 0.25f, 0.3f, 1f);
        private static readonly Color RecoveryTint = new Color(0.36f, 0.9f, 0.85f, 1f);

        [SerializeField] private CharacterIdentityCatalog catalog;
        [SerializeField] private WebStartGate webStartGate;
        [SerializeField] private PlayerLifeCycle playerLifeCycle;
        [SerializeField] private BlessingTargeting blessingTargeting;
        [SerializeField] private ExitGate exitGate;
        [SerializeField] private EnemyBase[] enemies = Array.Empty<EnemyBase>();
        [SerializeField] private GameObject cardRoot;
        [SerializeField] private Image portraitImage;
        [SerializeField] private Image frameImage;
        [SerializeField] private Text nameText;
        [SerializeField] private Text roleText;
        [SerializeField] private Text habitText;
        [SerializeField] private float holdSeconds = 2.6f;

        private Action<AttackPhase>[] phaseHandlers;
        private int introducedRoles;
        private bool initialized;
        private bool subscribed;
        private bool cardVisible;
        private float hideAtUnscaledTime;
        private CharacterIdentity currentIdentity;
        private CharacterExpression currentExpression;

        /// <summary>True while a card occupies the screen.</summary>
        public bool IsCardVisible => cardVisible;

        /// <summary>The cast member on the visible card, or null while hidden.</summary>
        public CharacterIdentity CurrentIdentity => cardVisible ? currentIdentity : null;

        /// <summary>The expression on the visible card.</summary>
        public CharacterExpression CurrentExpression => currentExpression;

        /// <summary>Seconds a raised card stays up.</summary>
        public float HoldSeconds => holdSeconds;

        private void Start()
        {
            // Enemies build their attack state machines in Awake, so the card cannot
            // observe them any earlier than the first Start pass.
            Initialize();
            Subscribe();
        }

        /// <summary>Validates wiring and prepares per-enemy observers exactly once.</summary>
        public void Initialize()
        {
            if (initialized)
            {
                return;
            }

            ValidateConfiguration();
            catalog.Validate();

            phaseHandlers = new Action<AttackPhase>[enemies.Length];
            for (var index = 0; index < enemies.Length; index++)
            {
                var enemyIndex = index;
                phaseHandlers[index] = phase => HandleEnemyPhaseChanged(enemyIndex, phase);
            }

            introducedRoles = 0;
            HideCard();
            initialized = true;
        }

        private void OnEnable()
        {
            if (!initialized)
            {
                return;
            }

            Subscribe();
        }

        private void OnDisable()
        {
            Unsubscribe();
            HideCard();
        }

        private void Update()
        {
            if (!cardVisible || Time.unscaledTime < hideAtUnscaledTime)
            {
                return;
            }

            HideCard();
        }

        private void Subscribe()
        {
            if (subscribed)
            {
                return;
            }

            for (var index = 0; index < enemies.Length; index++)
            {
                enemies[index].AttackState.PhaseChanged += phaseHandlers[index];
            }

            blessingTargeting.BlessingApplied += HandleBlessingApplied;
            exitGate.Entered += HandleExitEntered;
            playerLifeCycle.Died += HandlePlayerDied;
            playerLifeCycle.Reset += HandleRunReset;
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!subscribed)
            {
                return;
            }

            for (var index = 0; index < enemies.Length; index++)
            {
                enemies[index].AttackState.PhaseChanged -= phaseHandlers[index];
            }

            blessingTargeting.BlessingApplied -= HandleBlessingApplied;
            exitGate.Entered -= HandleExitEntered;
            playerLifeCycle.Died -= HandlePlayerDied;
            playerLifeCycle.Reset -= HandleRunReset;
            subscribed = false;
        }

        /// <summary>
        /// True once a trusted gesture has started the run. Enemies receive one behaviour
        /// tick on the frame a scene loads, because the frame's delta time is already fixed
        /// when the start gate zeroes the time scale. Without this guard that tick would
        /// spend a first encounter the player never saw.
        /// </summary>
        private bool RunHasStarted => webStartGate.IsStarted;

        /// <summary>
        /// First encounter. A cast member introduces itself the first time one of its
        /// enemies commits to a warning, which is the moment its habit becomes
        /// observable. Two archers share one rival, so the introduction is tracked per
        /// cast member instead of per enemy.
        /// </summary>
        private void HandleEnemyPhaseChanged(int enemyIndex, AttackPhase phase)
        {
            if (phase != AttackPhase.Warning || !RunHasStarted)
            {
                return;
            }

            var enemy = enemies[enemyIndex];
            if (!catalog.TryGetByDefinition(enemy.Definition, out var identity) || IsIntroduced(identity.Role))
            {
                return;
            }

            MarkIntroduced(identity.Role);
            ShowCard(identity, CharacterExpression.Neutral);
        }

        /// <summary>Blessing choice. The blessed rival answers with its confident face.</summary>
        private void HandleBlessingApplied(BlessingApplicationSignal signal)
        {
            if (!RunHasStarted)
            {
                return;
            }

            for (var index = 0; index < enemies.Length; index++)
            {
                var enemy = enemies[index];
                if (enemy.EntityId != signal.TargetEntityId)
                {
                    continue;
                }

                if (catalog.TryGetByDefinition(enemy.Definition, out var identity))
                {
                    MarkIntroduced(identity.Role);
                    ShowCard(identity, CharacterExpression.Confident);
                }

                return;
            }
        }

        /// <summary>True once this cast member has already introduced itself this attempt.</summary>
        public bool IsIntroduced(CharacterRole role)
        {
            return (introducedRoles & (1 << (int)role)) != 0;
        }

        private void MarkIntroduced(CharacterRole role)
        {
            introducedRoles |= 1 << (int)role;
        }

        private void HandleExitEntered()
        {
            if (!RunHasStarted)
            {
                return;
            }

            ShowCard(catalog.GetRequired(CharacterRole.Player), CharacterExpression.Confident);
        }

        private void HandlePlayerDied(DeathEvent deathEvent)
        {
            if (!RunHasStarted)
            {
                return;
            }

            ShowCard(catalog.GetRequired(CharacterRole.Player), CharacterExpression.Hurt);
        }

        /// <summary>
        /// A restart opens a fresh attempt, so introductions have to be earned again and
        /// a defeat card must not survive into the new run.
        /// </summary>
        private void HandleRunReset()
        {
            introducedRoles = 0;
            HideCard();
        }

        private void ShowCard(CharacterIdentity identity, CharacterExpression expression)
        {
            currentIdentity = identity;
            currentExpression = expression;
            nameText.text = identity.DisplayName;
            roleText.text = ComposeRoleLine(identity);
            habitText.text = identity.HabitLine;
            portraitImage.sprite = identity.GetPortrait(expression);
            portraitImage.enabled = true;
            frameImage.color = ComposeFrameColor(identity.MotifColor, expression);
            hideAtUnscaledTime = Time.unscaledTime + holdSeconds;
            cardVisible = true;
            cardRoot.SetActive(true);
        }

        private void HideCard()
        {
            cardVisible = false;
            currentIdentity = null;
            cardRoot.SetActive(false);
        }

        /// <summary>Joins the age line and the concept line, skipping the age for the non-human cast.</summary>
        public static string ComposeRoleLine(CharacterIdentity identity)
        {
            if (identity == null)
            {
                throw new ArgumentNullException(nameof(identity));
            }

            return string.IsNullOrWhiteSpace(identity.AgeLine)
                ? identity.RoleLine
                : identity.AgeLine + "  ·  " + identity.RoleLine;
        }

        /// <summary>
        /// Blends the motif colour toward the expression so the card reads as the same
        /// character in a different state even while a representative portrait stands in
        /// for every expression.
        /// </summary>
        public static Color ComposeFrameColor(Color motifColor, CharacterExpression expression)
        {
            var tint = expression switch
            {
                CharacterExpression.Neutral => NeutralTint,
                CharacterExpression.Confident => ConfidentTint,
                CharacterExpression.Hurt => HurtTint,
                CharacterExpression.Recovery => RecoveryTint,
                _ => throw new ArgumentOutOfRangeException(nameof(expression))
            };

            var blended = Color.Lerp(motifColor, tint, 0.35f);
            blended.a = 1f;
            return blended;
        }

        private void ValidateConfiguration()
        {
            if (catalog == null || playerLifeCycle == null || blessingTargeting == null || exitGate == null ||
                webStartGate == null)
            {
                throw new InvalidOperationException(
                    "Character appeal presenter requires the identity catalog, start gate, player life cycle, blessing targeting and exit gate.");
            }

            if (cardRoot == null || portraitImage == null || frameImage == null ||
                nameText == null || roleText == null || habitText == null)
            {
                throw new InvalidOperationException("Character appeal presenter requires its card views.");
            }

            if (holdSeconds <= 0f)
            {
                throw new InvalidOperationException("Character appeal presenter requires a positive hold duration.");
            }

            if (enemies == null || enemies.Length == 0)
            {
                throw new InvalidOperationException("Character appeal presenter requires the enemy roster it introduces.");
            }

            for (var index = 0; index < enemies.Length; index++)
            {
                var enemy = enemies[index];
                if (enemy == null || enemy.AttackState == null || enemy.Definition == null)
                {
                    throw new InvalidOperationException(
                        "Character appeal presenter requires enemies with attack state and enemy data.");
                }
            }

            if (portraitImage.raycastTarget || frameImage.raycastTarget ||
                nameText.raycastTarget || roleText.raycastTarget || habitText.raycastTarget)
            {
                throw new InvalidOperationException(
                    "Character appeal card must not take raycasts so blessing clicks keep reaching the world.");
            }
        }
    }
}
