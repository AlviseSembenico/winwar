using System;
using System.Collections.Generic;
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

        // Event for right-click tap (not drag)
        public event Action<Vector3> OnRightClickTap;
        public event Action<Vector3, Vector3> OnRightDragCompleted;
        public event Action<List<Vector3>> OnRightPathUpdated;
        public event Action<List<Vector3>> OnRightPathCompleted;
        public event Action<Vector3> OnLeftClickTap;
        public event Action<Vector3, Vector3> OnLeftDragCompleted;

        // SimulationManager uses this to reserve city-to-city drags for troop orders.
        public Func<Vector3, bool> ShouldReserveRightDrag;
        public Func<Vector3, bool> ShouldReserveLeftDrag;
        public Func<bool> ShouldDrawRightPath;

        private Camera cam;
        private Vector3 dragOriginWorld;
        private Vector3 dragStartScreen;
        private bool isDragging = false;
        private bool hasMovedBeyondThreshold = false;
        private bool isReservedRightDrag = false;
        private readonly List<Vector3> rightDragPath = new List<Vector3>();
        private Vector3 leftDragOriginWorld;
        private Vector3 leftDragStartScreen;
        private bool isReservedLeftDrag;
        private bool leftDragMovedBeyondThreshold;
        private float targetZoom;
        private Vector3 targetPosition;
        private Vector3 panVelocity;

        private int mapWidth = 1000;
        private int mapHeight = 1000;

        private void Awake()
        {
            cam = GetComponent<Camera>();
            ConfigureCamera2D();
        }

        private void ConfigureCamera2D()
        {
            if (cam == null) cam = GetComponent<Camera>();
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = backgroundColor;
            targetPosition = transform.position;
            targetZoom = cam.orthographicSize;
        }

        public void FocusOnMap(int width, int height)
        {
            if (cam == null) ConfigureCamera2D();

            mapWidth = width;
            mapHeight = height;

            transform.position = new Vector3(width * 0.5f, height * 0.5f, -10f);
            targetPosition = transform.position;

            float vertExtent = height * 0.5f;
            float horzExtent = (width * 0.5f) / Mathf.Max(0.1f, cam.aspect);
            float fitSize = Mathf.Max(vertExtent, horzExtent);

            maxZoom = fitSize * 1.6f;
            minZoom = 10f;
            cam.orthographicSize = fitSize;
            targetZoom = fitSize;
        }

        private void Update()
        {
            HandleKeyboardPan();
            HandleMouseDragPan();
            HandleLeftCityDrag();
            HandleScrollZoom();

            transform.position = Vector3.SmoothDamp(transform.position, targetPosition, ref panVelocity, smoothTime);
            cam.orthographicSize = Mathf.Lerp(cam.orthographicSize, targetZoom, Time.deltaTime * 15f);

            ClampPosition();
        }

        private void HandleKeyboardPan()
        {
            float h = 0f;
            float v = 0f;

#if ENABLE_INPUT_SYSTEM
            if (UnityEngine.InputSystem.Keyboard.current != null)
            {
                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (kb.leftArrowKey.isPressed) h -= 1f;
                if (kb.rightArrowKey.isPressed) h += 1f;
                if (kb.downArrowKey.isPressed) v -= 1f;
                if (kb.upArrowKey.isPressed) v += 1f;
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            if (h == 0f && v == 0f)
            {
                if (Input.GetKey(KeyCode.LeftArrow)) h -= 1f;
                if (Input.GetKey(KeyCode.RightArrow)) h += 1f;
                if (Input.GetKey(KeyCode.DownArrow)) v -= 1f;
                if (Input.GetKey(KeyCode.UpArrow)) v += 1f;
            }
#endif

            if (Mathf.Abs(h) > 0.01f || Mathf.Abs(v) > 0.01f)
            {
                float speedMultiplier = cam.orthographicSize / 500f;
                Vector3 move = new Vector3(h, v, 0f).normalized * keyboardPanSpeed * Mathf.Max(0.2f, speedMultiplier) * Time.deltaTime;
                targetPosition += move;
            }
        }

        private Vector3 GetMouseScreenPosition()
        {
#if ENABLE_INPUT_SYSTEM
            if (UnityEngine.InputSystem.Mouse.current != null)
            {
                Vector2 mPos = UnityEngine.InputSystem.Mouse.current.position.ReadValue();
                return new Vector3(mPos.x, mPos.y, 0f);
            }
#endif
            return Input.mousePosition;
        }

        private bool IsDragButtonPressed()
        {
#if ENABLE_INPUT_SYSTEM
            if (UnityEngine.InputSystem.Mouse.current != null)
            {
                var mouse = UnityEngine.InputSystem.Mouse.current;
                if (mouse.rightButton.isPressed || mouse.middleButton.isPressed) return true;
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetMouseButton(1) || Input.GetMouseButton(2)) return true;
#endif
            return false;
        }

        private bool WasDragButtonDown()
        {
#if ENABLE_INPUT_SYSTEM
            if (UnityEngine.InputSystem.Mouse.current != null)
            {
                var mouse = UnityEngine.InputSystem.Mouse.current;
                if (mouse.rightButton.wasPressedThisFrame || mouse.middleButton.wasPressedThisFrame) return true;
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(2)) return true;
#endif
            return false;
        }

        private bool WasRightButtonUp()
        {
#if ENABLE_INPUT_SYSTEM
            if (UnityEngine.InputSystem.Mouse.current != null)
            {
                return UnityEngine.InputSystem.Mouse.current.rightButton.wasReleasedThisFrame;
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetMouseButtonUp(1)) return true;
#endif
            return false;
        }

        private bool WasRightButtonDown()
        {
#if ENABLE_INPUT_SYSTEM
            if (UnityEngine.InputSystem.Mouse.current != null)
            {
                return UnityEngine.InputSystem.Mouse.current.rightButton.wasPressedThisFrame;
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetMouseButtonDown(1)) return true;
#endif
            return false;
        }

        private bool IsLeftButtonPressed()
        {
#if ENABLE_INPUT_SYSTEM
            if (UnityEngine.InputSystem.Mouse.current != null)
                return UnityEngine.InputSystem.Mouse.current.leftButton.isPressed;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetMouseButton(0);
#else
            return false;
#endif
        }

        private bool WasLeftButtonDown()
        {
#if ENABLE_INPUT_SYSTEM
            if (UnityEngine.InputSystem.Mouse.current != null)
                return UnityEngine.InputSystem.Mouse.current.leftButton.wasPressedThisFrame;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetMouseButtonDown(0);
#else
            return false;
#endif
        }

        private bool WasLeftButtonUp()
        {
#if ENABLE_INPUT_SYSTEM
            if (UnityEngine.InputSystem.Mouse.current != null)
                return UnityEngine.InputSystem.Mouse.current.leftButton.wasReleasedThisFrame;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetMouseButtonUp(0);
#else
            return false;
#endif
        }

        private void HandleMouseDragPan()
        {
            Vector3 mouseScreen = GetMouseScreenPosition();

            if (WasDragButtonDown())
            {
                bool startedWithRightButton = WasRightButtonDown();
                dragStartScreen = mouseScreen;
                dragOriginWorld = cam.ScreenToWorldPoint(mouseScreen);
                isDragging = false;
                hasMovedBeyondThreshold = false;
                isReservedRightDrag = startedWithRightButton
                    && ((ShouldReserveRightDrag != null && ShouldReserveRightDrag(dragOriginWorld))
                        || (ShouldDrawRightPath != null && ShouldDrawRightPath()));
                rightDragPath.Clear();
                if (isReservedRightDrag)
                    rightDragPath.Add(dragOriginWorld);
            }

            if (IsDragButtonPressed())
            {
                if (Vector3.Distance(mouseScreen, dragStartScreen) > 8f)
                {
                    hasMovedBeyondThreshold = true;
                    isDragging = true;
                }

                if (isDragging && !isReservedRightDrag)
                {
                    Vector3 currentWorld = cam.ScreenToWorldPoint(mouseScreen);
                    Vector3 diff = dragOriginWorld - currentWorld;
                    targetPosition += diff;
                    dragOriginWorld = cam.ScreenToWorldPoint(mouseScreen);
                }

                if (isReservedRightDrag)
                {
                    Vector3 point = cam.ScreenToWorldPoint(mouseScreen);
                    if (Vector3.Distance(point, rightDragPath[rightDragPath.Count - 1]) > 1f)
                    {
                        rightDragPath.Add(point);
                        OnRightPathUpdated?.Invoke(rightDragPath);
                    }
                }
            }

            // Check if right button was released without significant drag (a clean tap!)
            if (WasRightButtonUp())
            {
                Vector3 clickWorld = cam.ScreenToWorldPoint(mouseScreen);
                if (isReservedRightDrag && hasMovedBeyondThreshold)
                {
                    OnRightDragCompleted?.Invoke(dragOriginWorld, clickWorld);
                    rightDragPath.Add(clickWorld);
                    OnRightPathCompleted?.Invoke(rightDragPath);
                }
                else if (!hasMovedBeyondThreshold)
                {
                    OnRightClickTap?.Invoke(clickWorld);
                }
                isDragging = false;
                isReservedRightDrag = false;
                rightDragPath.Clear();
            }
        }

        private void HandleLeftCityDrag()
        {
            Vector3 mouseScreen = GetMouseScreenPosition();

            if (WasLeftButtonDown())
            {
                leftDragStartScreen = mouseScreen;
                leftDragOriginWorld = cam.ScreenToWorldPoint(mouseScreen);
                isReservedLeftDrag = ShouldReserveLeftDrag != null && ShouldReserveLeftDrag(leftDragOriginWorld);
                leftDragMovedBeyondThreshold = false;

                // Resolve selections immediately on press. This makes returning a
                // selected field army to a city reliable even when the city icon is
                // underneath that army's overlay marker.
                OnLeftClickTap?.Invoke(leftDragOriginWorld);
            }

            if (isReservedLeftDrag && IsLeftButtonPressed()
                && Vector3.Distance(mouseScreen, leftDragStartScreen) > 8f)
            {
                leftDragMovedBeyondThreshold = true;
            }

            if (WasLeftButtonUp())
            {
                if (isReservedLeftDrag && leftDragMovedBeyondThreshold)
                {
                    OnLeftDragCompleted?.Invoke(leftDragOriginWorld, cam.ScreenToWorldPoint(mouseScreen));
                }
                isReservedLeftDrag = false;
            }
        }

        private void HandleScrollZoom()
        {
            float scroll = 0f;

#if ENABLE_INPUT_SYSTEM
            if (UnityEngine.InputSystem.Mouse.current != null)
            {
                scroll = UnityEngine.InputSystem.Mouse.current.scroll.ReadValue().y * 0.01f;
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Mathf.Abs(scroll) < 0.001f)
            {
                scroll = Input.GetAxis("Mouse ScrollWheel");
            }
#endif

            if (Mathf.Abs(scroll) > 0.001f)
            {
                Vector3 mouseWorldBefore = cam.ScreenToWorldPoint(GetMouseScreenPosition());

                float zoomDelta = -scroll * zoomSensitivity * (targetZoom * 0.1f);
                targetZoom = Mathf.Clamp(targetZoom + zoomDelta, minZoom, maxZoom);

                float zoomFactor = targetZoom / cam.orthographicSize;
                targetPosition = mouseWorldBefore + (targetPosition - mouseWorldBefore) * zoomFactor;
                targetPosition.z = -10f;
            }
        }

        private void ClampPosition()
        {
            float camVertExtent = cam.orthographicSize;
            float camHorzExtent = cam.orthographicSize * cam.aspect;

            float minX = -camHorzExtent * 0.5f;
            float maxX = mapWidth + camHorzExtent * 0.5f;
            float minY = -camVertExtent * 0.5f;
            float maxY = mapHeight + camVertExtent * 0.5f;

            targetPosition.x = Mathf.Clamp(targetPosition.x, minX, maxX);
            targetPosition.y = Mathf.Clamp(targetPosition.y, minY, maxY);
            targetPosition.z = -10f;
        }
    }
}
