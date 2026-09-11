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

        private Camera cam;
        private Vector3 dragOrigin;
        private bool isDragging = false;
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
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) h -= 1f;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) h += 1f;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) v -= 1f;
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) v += 1f;
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            if (h == 0f && v == 0f)
            {
                h = Input.GetAxisRaw("Horizontal");
                v = Input.GetAxisRaw("Vertical");
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

        private void HandleMouseDragPan()
        {
            Vector3 mouseScreen = GetMouseScreenPosition();

            if (WasDragButtonDown())
            {
                dragOrigin = cam.ScreenToWorldPoint(mouseScreen);
                isDragging = true;
            }

            if (isDragging && IsDragButtonPressed())
            {
                Vector3 currentPos = cam.ScreenToWorldPoint(mouseScreen);
                Vector3 diff = dragOrigin - currentPos;
                targetPosition += diff;
                dragOrigin = cam.ScreenToWorldPoint(mouseScreen);
            }
            else
            {
                isDragging = false;
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
