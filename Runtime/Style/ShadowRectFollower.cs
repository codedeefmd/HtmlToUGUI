using UnityEngine;

namespace Xxhq.Htmltougui
{
    /// <summary>
    /// 外阴影与目标元素同级，因而能显示在目标自身的 Mask 外面。
    /// 此组件只同步 RectTransform，不参与父级布局，也不修改目标元素。
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(RectTransform))]
    public sealed class ShadowRectFollower : MonoBehaviour
    {
        [SerializeField] private RectTransform _target;
        [SerializeField] private int _outerOrder;
        [SerializeField] private bool _bound;
        private BoxShadowGraphic _graphic;

        public void SetTarget(RectTransform target, int outerOrder = 0)
        {
            _target = target;
            _outerOrder = outerOrder;
            _bound = true;
            Sync();
        }

        private void OnEnable()
        {
            Canvas.willRenderCanvases += Sync;
            RectTransform.reapplyDrivenProperties += OnReapplyDrivenProperties;
            Sync();
        }

        private void OnDisable()
        {
            Canvas.willRenderCanvases -= Sync;
            RectTransform.reapplyDrivenProperties -= OnReapplyDrivenProperties;
        }

        private void LateUpdate()
        {
            Sync();
        }

        private void OnReapplyDrivenProperties(RectTransform driven)
        {
            if (driven == _target) Sync();
        }

        private void Sync()
        {
            if (!_bound) return;
            // 阴影与目标是同级对象；目标被隐藏或删除时，不能留下可见的孤立阴影。
            if (_graphic == null) _graphic = GetComponent<BoxShadowGraphic>();
            if (_bound && _target == null)
            {
                // 目标已删除：注销回调并移除不再使用的同级阴影。
                enabled = false;
                if (Application.isPlaying) Destroy(gameObject);
                else DestroyImmediate(gameObject);
                return;
            }
            if (_graphic != null)
            {
                _graphic.enabled = _target != null && _target.gameObject.activeInHierarchy;
                if (_target != null)
                {
                    CanvasGroup group = _target.GetComponent<CanvasGroup>();
                    _graphic.SetTargetAlpha(group != null && group.enabled ? group.alpha : 1f);
                }
            }
            if (_target == null) return;
            var shadow = (RectTransform)transform;
            if (shadow.parent != _target.parent)
                shadow.SetParent(_target.parent, false);

            if (shadow.anchorMin != _target.anchorMin) shadow.anchorMin = _target.anchorMin;
            if (shadow.anchorMax != _target.anchorMax) shadow.anchorMax = _target.anchorMax;
            if (shadow.pivot != _target.pivot) shadow.pivot = _target.pivot;
            if (shadow.sizeDelta != _target.sizeDelta) shadow.sizeDelta = _target.sizeDelta;
            if (shadow.anchoredPosition3D != _target.anchoredPosition3D)
                shadow.anchoredPosition3D = _target.anchoredPosition3D;
            if (shadow.localRotation != _target.localRotation) shadow.localRotation = _target.localRotation;
            if (shadow.localScale != _target.localScale) shadow.localScale = _target.localScale;
            int desiredIndex = Mathf.Max(0, _target.GetSiblingIndex() - 1 - _outerOrder);
            if (shadow.GetSiblingIndex() != desiredIndex)
                shadow.SetSiblingIndex(desiredIndex);
        }
    }
}
