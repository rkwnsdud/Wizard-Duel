using UnityEngine;
using UnityEngine.EventSystems;

// 시작 화면 메뉴 버튼에 붙인다. 마우스를 올리면 선택되고, 선택된 동안만 번개 표시와 설명을 보인다.
public class MenuButton : MonoBehaviour, IPointerEnterHandler, ISelectHandler, IDeselectHandler
{
    [SerializeField] GameObject[] showWhenSelected;

    public void OnPointerEnter(PointerEventData eventData) => EventSystem.current.SetSelectedGameObject(gameObject);
    public void OnSelect(BaseEventData eventData) => Show(true);
    public void OnDeselect(BaseEventData eventData) => Show(false);

    void Show(bool visible)
    {
        foreach (GameObject target in showWhenSelected) target.SetActive(visible);
    }
}
