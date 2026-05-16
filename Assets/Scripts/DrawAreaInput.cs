using UnityEngine;
using UnityEngine.EventSystems;

[RequireComponent(typeof(UnityEngine.UI.RawImage))]
public class DrawAreaInput : MonoBehaviour,
    IPointerDownHandler, IPointerUpHandler,
    IBeginDragHandler, IDragHandler, IEndDragHandler
{
    const float ClickMoveThresholdPixels = 12f;

    Voronoi_Configuration config;
    Vector2 pointerDownScreenPos;
    bool editLeftDown;

    public void Init(Voronoi_Configuration configuration)
    {
        config = configuration;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (config == null) return;
        config.HandleDrawAreaPointerDown(eventData);

        if (config.CursorMode == CursorToolMode.Edit
            && eventData.button == PointerEventData.InputButton.Left)
        {
            editLeftDown = true;
            pointerDownScreenPos = eventData.position;
        }
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (config == null) return;

        if (editLeftDown
            && eventData.button == PointerEventData.InputButton.Left
            && Vector2.Distance(eventData.position, pointerDownScreenPos) <= ClickMoveThresholdPixels)
        {
            config.HandleDrawAreaLeftClick(eventData);
        }

        editLeftDown = false;
        config.HandleDrawAreaPointerUp(eventData);
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (config == null || config.CursorMode != CursorToolMode.Drag) return;
        if (eventData.button != PointerEventData.InputButton.Left) return;
        config.HandleDrawAreaDrag(eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (config == null || config.CursorMode != CursorToolMode.Drag) return;
        config.HandleDrawAreaDrag(eventData);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (config == null) return;
        config.HandleDrawAreaEndDrag(eventData);
    }
}
