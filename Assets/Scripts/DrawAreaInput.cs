using UnityEngine;
using UnityEngine.EventSystems;

[RequireComponent(typeof(UnityEngine.UI.RawImage))]
public class DrawAreaInput : MonoBehaviour,
    IPointerDownHandler, IPointerUpHandler,
    IBeginDragHandler, IDragHandler, IEndDragHandler
{
    const float ClickMoveThresholdPixels = 12f;

    DrawAreaSeedInteraction interaction;
    Vector2 pointerDownScreenPos;
    bool editLeftDown;

    public void Init(DrawAreaSeedInteraction seedInteraction)
    {
        interaction = seedInteraction;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (interaction == null) return;
        interaction.HandlePointerDown(eventData);

        if (interaction.CursorMode == CursorToolMode.Edit
            && eventData.button == PointerEventData.InputButton.Left)
        {
            editLeftDown = true;
            pointerDownScreenPos = eventData.position;
        }
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (interaction == null) return;

        if (editLeftDown
            && eventData.button == PointerEventData.InputButton.Left
            && Vector2.Distance(eventData.position, pointerDownScreenPos) <= ClickMoveThresholdPixels)
        {
            interaction.HandleLeftClick(eventData);
        }

        editLeftDown = false;
        interaction.HandlePointerUp(eventData);
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (interaction == null || interaction.CursorMode != CursorToolMode.Drag) return;
        if (eventData.button != PointerEventData.InputButton.Left) return;
        interaction.HandleDrag(eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (interaction == null || interaction.CursorMode != CursorToolMode.Drag) return;
        interaction.HandleDrag(eventData);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (interaction == null) return;
        interaction.HandleEndDrag(eventData);
    }
}
