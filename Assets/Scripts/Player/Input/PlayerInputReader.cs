using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 读取持续输入，并将离散操作写入共享输入缓冲。
/// </summary>
public class PlayerInputReader : MonoBehaviour, IPlayerInputSource
{
    private InputSystem_Actions inputActions;
    private IPlayerActionBuffer actionBuffer;

    public Vector2 MoveInput { get; private set; }
    public Vector2 LookInput { get; private set; }
    //WalkToggle 的当前信号，不代表 Gameplay 当前有效的 Walk 模式。
    public bool IsWalkMode { get; private set; }
    public PlayerFacingMode FacingMode { get; private set; } = PlayerFacingMode.MovementAligned;

    private void Awake()
    {
        inputActions = new InputSystem_Actions();
    }

    public void Init(IPlayerActionBuffer actionBuffer)
    {
        this.actionBuffer = actionBuffer;
    }

    private void OnEnable()
    {
        if (inputActions == null)
        {
            inputActions = new InputSystem_Actions();
        }

        RegisterInputCallbacks();
        inputActions.Player.Enable();
    }

    private void OnDisable()
    {
        if (inputActions != null)
        {
            inputActions.Player.Disable();
            UnregisterInputCallbacks();
        }

        MoveInput = Vector2.zero;
        LookInput = Vector2.zero;
        IsWalkMode = false;
        FacingMode = PlayerFacingMode.MovementAligned;
        actionBuffer?.Clear(PlayerBufferedAction.Jump);
        actionBuffer?.Clear(PlayerBufferedAction.Dodge);
    }

    private void OnDestroy()
    {
        inputActions?.Dispose();
    }

    private void RegisterInputCallbacks()
    {
        inputActions.Player.Move.performed += OnMovePerformed;
        inputActions.Player.Move.canceled += OnMoveCanceled;
        inputActions.Player.Look.performed += OnLookPerformed;
        inputActions.Player.Look.canceled += OnLookCanceled;
        inputActions.Player.Jump.started += OnJumpStarted;
        inputActions.Player.Dodge.started += OnDodgeStarted;
        inputActions.Player.WalkToggle.started += OnWalkToggleStarted;
        inputActions.Player.FacingModeToggle.started += OnFacingModeToggleStarted;
    }

    private void UnregisterInputCallbacks()
    {
        inputActions.Player.Move.performed -= OnMovePerformed;
        inputActions.Player.Move.canceled -= OnMoveCanceled;
        inputActions.Player.Look.performed -= OnLookPerformed;
        inputActions.Player.Look.canceled -= OnLookCanceled;
        inputActions.Player.Jump.started -= OnJumpStarted;
        inputActions.Player.Dodge.started -= OnDodgeStarted;
        inputActions.Player.WalkToggle.started -= OnWalkToggleStarted;
        inputActions.Player.FacingModeToggle.started -= OnFacingModeToggleStarted;
    }

    private void OnMovePerformed(InputAction.CallbackContext context) => MoveInput = context.ReadValue<Vector2>();
    private void OnMoveCanceled(InputAction.CallbackContext context) => MoveInput = Vector2.zero;
    private void OnLookPerformed(InputAction.CallbackContext context) => LookInput = context.ReadValue<Vector2>();
    private void OnLookCanceled(InputAction.CallbackContext context) => LookInput = Vector2.zero;
    private void OnJumpStarted(InputAction.CallbackContext context) => actionBuffer.Buffer(PlayerBufferedAction.Jump);
    private void OnDodgeStarted(InputAction.CallbackContext context) => actionBuffer.Buffer(PlayerBufferedAction.Dodge);
    private void OnWalkToggleStarted(InputAction.CallbackContext context) => IsWalkMode = !IsWalkMode;
    private void OnFacingModeToggleStarted(InputAction.CallbackContext context) => FacingMode = FacingMode == PlayerFacingMode.MovementAligned ? PlayerFacingMode.Independent : PlayerFacingMode.MovementAligned;
}
