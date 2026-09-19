using System;
using UnityEngine;

namespace AgesOfConflict
{
    [RequireComponent(typeof(Camera))]
    public class CameraController : MonoBehaviour
    {
        [Header("Pan Settings")]
        public float keyboardPanSpeed = 500f;
        public float mouseDragSensitivity = 1f;
        [Header("Zoom Settings")]
        public float zoomSensitivity = 50f;
        public float minZoom = 10f;
        public float maxZoom = 1200f;
        public float smoothTime = 0.08f;
        [Header("Background Styling")]
        public Color backgroundColor = new Color(0.04f, 0.07f, 0.12f, 1f);

        public event Action<Vector3> OnRightClickTap;
        public event Action<Vector3> OnLeftClickTap;
        public event Action<Vector3> OnAttackDirectionDragStart;
        public event Action<Vector3> OnAttackDirectionDrag;
        public event Action<Vector3> OnAttackDirectionDragEnd;
        public event Action<Vector3> OnDefensiveWallDragStart;
        public event Action<Vector3> OnDefensiveWallDrag;
        public event Action<Vector3> OnDefensiveWallDragEnd;

        public bool InputBlocked { get; set; }

        private Camera cam;
        private Vector3 dragOriginWorld, dragStartScreen, targetPosition, panVelocity;
        private float targetZoom;
        private bool dragging, moved;
        private bool attackDirectionDragging;
        private bool defensiveWallDragging;
        private int mapWidth = 1000, mapHeight = 1000;

        private void Awake()
        {
            cam = GetComponent<Camera>();
            // A rotated or scaled camera transform shears the orthographic view of the map quad,
            // so normalize whatever transform the scene file ships with.
            transform.rotation = Quaternion.identity; transform.localScale = Vector3.one;
            cam.orthographic = true; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = backgroundColor;
            targetPosition = transform.position; targetZoom = cam.orthographicSize;
        }

        public void FocusOnMap(int width, int height)
        {
            mapWidth = width; mapHeight = height;
            targetPosition = transform.position = new Vector3(width * .5f, height * .5f, -10f);
            float fit = Mathf.Max(height * .5f, width * .5f / Mathf.Max(.1f, cam.aspect));
            minZoom = 10f; maxZoom = fit * 1.6f; targetZoom = cam.orthographicSize = fit;
        }

        private void Update()
        {
            if (InputBlocked)
            {
                dragging = moved = attackDirectionDragging = defensiveWallDragging = false;
                panVelocity = Vector3.zero;
                targetPosition = transform.position;
                targetZoom = cam.orthographicSize;
                return;
            }
            HandleKeyboardPan(); HandleMouse(); HandleScrollZoom();
            transform.position = Vector3.SmoothDamp(transform.position, targetPosition, ref panVelocity, smoothTime);
            cam.orthographicSize = Mathf.Lerp(cam.orthographicSize, targetZoom, Time.deltaTime * 15f);
            ClampPosition();
        }

        private void HandleKeyboardPan()
        {
            float h = Input.GetAxisRaw("Horizontal"), v = Input.GetAxisRaw("Vertical");
#if ENABLE_INPUT_SYSTEM
            if (UnityEngine.InputSystem.Keyboard.current != null)
            {
                var k = UnityEngine.InputSystem.Keyboard.current;
                h = (k.rightArrowKey.isPressed ? 1 : 0) - (k.leftArrowKey.isPressed ? 1 : 0);
                v = (k.upArrowKey.isPressed ? 1 : 0) - (k.downArrowKey.isPressed ? 1 : 0);
            }
#endif
            if (Mathf.Abs(h) > .01f || Mathf.Abs(v) > .01f)
                targetPosition += new Vector3(h, v).normalized * keyboardPanSpeed * Mathf.Max(.2f, cam.orthographicSize / 500f) * Time.deltaTime;
        }

        private Vector3 MousePosition()
        {
#if ENABLE_INPUT_SYSTEM
            if (UnityEngine.InputSystem.Mouse.current != null) { Vector2 p = UnityEngine.InputSystem.Mouse.current.position.ReadValue(); return new Vector3(p.x, p.y); }
#endif
            return Input.mousePosition;
        }
        private bool ButtonDown(int button) => Input.GetMouseButtonDown(button);
        private bool Button(int button) => Input.GetMouseButton(button);
        private bool ButtonUp(int button) => Input.GetMouseButtonUp(button);

        private bool IsAttackDirectionModifierPressed()
        {
#if ENABLE_INPUT_SYSTEM
            if (UnityEngine.InputSystem.Keyboard.current != null)
                return UnityEngine.InputSystem.Keyboard.current.aKey.isPressed;
#endif
            return Input.GetKey(KeyCode.A);
        }

        private bool IsDefensiveWallModifierPressed()
        {
#if ENABLE_INPUT_SYSTEM
            if (UnityEngine.InputSystem.Keyboard.current != null)
                return UnityEngine.InputSystem.Keyboard.current.dKey.isPressed;
#endif
            return Input.GetKey(KeyCode.D);
        }

        private void HandleMouse()
        {
            Vector3 mouse = MousePosition();
            Vector3 worldMouse = cam.ScreenToWorldPoint(mouse);
            if (ButtonDown(0))
            {
                if (IsDefensiveWallModifierPressed())
                {
                    defensiveWallDragging = true;
                    OnDefensiveWallDragStart?.Invoke(worldMouse);
                }
                else if (IsAttackDirectionModifierPressed())
                {
                    attackDirectionDragging = true;
                    OnAttackDirectionDragStart?.Invoke(worldMouse);
                }
                else
                    OnLeftClickTap?.Invoke(worldMouse);
            }
            if (attackDirectionDragging && Button(0))
                OnAttackDirectionDrag?.Invoke(worldMouse);
            if (attackDirectionDragging && ButtonUp(0))
            {
                OnAttackDirectionDragEnd?.Invoke(worldMouse);
                attackDirectionDragging = false;
            }
            if (defensiveWallDragging && Button(0))
                OnDefensiveWallDrag?.Invoke(worldMouse);
            if (defensiveWallDragging && ButtonUp(0))
            {
                OnDefensiveWallDragEnd?.Invoke(worldMouse);
                defensiveWallDragging = false;
            }
            if (ButtonDown(1) || ButtonDown(2)) { dragStartScreen = mouse; dragOriginWorld = cam.ScreenToWorldPoint(mouse); dragging = true; moved = false; }
            if (dragging && (Button(1) || Button(2)))
            {
                if (Vector3.Distance(mouse, dragStartScreen) > 8f) moved = true;
                if (moved)
                {
                    Vector3 current = cam.ScreenToWorldPoint(mouse);
                    targetPosition += (dragOriginWorld - current) * mouseDragSensitivity;
                    dragOriginWorld = current;
                }
            }
            if (dragging && (ButtonUp(1) || ButtonUp(2)))
            {
                if (!moved && ButtonUp(1)) OnRightClickTap?.Invoke(cam.ScreenToWorldPoint(mouse));
                dragging = false;
            }
        }

        private void HandleScrollZoom()
        {
            float scroll = Input.GetAxis("Mouse ScrollWheel");
#if ENABLE_INPUT_SYSTEM
            if (UnityEngine.InputSystem.Mouse.current != null) scroll = UnityEngine.InputSystem.Mouse.current.scroll.ReadValue().y * .01f;
#endif
            if (Mathf.Abs(scroll) < .001f) return;
            Vector3 before = cam.ScreenToWorldPoint(MousePosition());
            targetZoom = Mathf.Clamp(targetZoom - scroll * zoomSensitivity * targetZoom * .1f, minZoom, maxZoom);
            targetPosition = before + (targetPosition - before) * (targetZoom / cam.orthographicSize); targetPosition.z = -10f;
        }

        private void ClampPosition()
        {
            float vertical = cam.orthographicSize, horizontal = vertical * cam.aspect;
            targetPosition.x = Mathf.Clamp(targetPosition.x, -horizontal * .5f, mapWidth + horizontal * .5f);
            targetPosition.y = Mathf.Clamp(targetPosition.y, -vertical * .5f, mapHeight + vertical * .5f);
            targetPosition.z = -10f;
        }
    }
}
