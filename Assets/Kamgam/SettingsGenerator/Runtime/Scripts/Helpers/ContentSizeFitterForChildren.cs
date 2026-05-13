using System.Linq;
using UnityEngine;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Kamgam.SettingsGenerator
{
    [RequireComponent(typeof(RectTransform))]
    public class ContentSizeFitterForChildren : MonoBehaviour
    {
        [Tooltip("Uncheck to avoid changes to width.")]
        public bool FitWidth = true;
        [Tooltip("Uncheck to avoid changes to height.")]
        public bool FitHeight = true;

        [Header("Settings")]
        [Tooltip("Is added to the calculated width (distribution is based on the pivot/anchors).")]
        public float AdditionalWidth = 0;
        [Tooltip("Is added to the calculated height (distribution is based on the pivot/anchors).")]
        public float AdditionalHeight = 0;

        [Tooltip("Uses a very simple logic to check whether or not to refresh (refreshes if the child count changes).")]
        public bool AutoRefresh = true;

        [Tooltip("Always refresh on each Update() call. Use it only if AutoRefresh is not sufficient, e.g. if the sizes of the children change all the time.")]
        public bool AlwaysRefresh = false;

        public RectTransform[] IgnoreList;

        public int ForceUpdateFirstNFrames = 0;
        protected int framesInUpdate = 0;

        protected RectTransform rectTransform;
        public RectTransform RectTransform
        {
            get
            {
                if (rectTransform == null)
                {
                    rectTransform = this.GetComponent<RectTransform>();
                }
                return rectTransform;
            }
        }
        protected bool isDirty;
        protected int lastChildCount;

        private void Awake()
        {
            isDirty = true;
            lastChildCount = -1;
        }

        private void OnEnable()
        {
            isDirty = false;
            updateSize();
        }

        public void Update()
        {
            if( AlwaysRefresh || ForceUpdateFirstNFrames > 0 && framesInUpdate++ < ForceUpdateFirstNFrames)
            {
                isDirty = true;
            }
            else
            {
                if( AutoRefresh )
                {
                    if( lastChildCount != transform.childCount )
                    {
                        isDirty = true;
                    }
                }
            }

            if (isDirty)
            {
                isDirty = false;
                updateSize();
            }
        }

        public void Refresh()
        {
            updateSize();
        }

        protected void updateSize()
        {
            if (FitWidth || FitHeight)
            {
                lastChildCount = transform.childCount;
                var bounds = calculateShallowBounds(RectTransform.parent, RectTransform);
                if (FitWidth)
                {
                    RectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, (bounds.size.x / RectTransform.localScale.x) + AdditionalWidth);
                }
                if (FitHeight)
                {
                    RectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, (bounds.size.y / RectTransform.localScale.y) + AdditionalHeight);
                }
                RectTransform.ForceUpdateRectTransforms();
            }
        }

        protected Vector3[] corners = new Vector3[4];

        protected Bounds calculateShallowBounds(Transform root, Transform child)
        {
            if (child.childCount > 0)
            {
                Vector3 vMin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
                Vector3 vMax = new Vector3(float.MinValue, float.MinValue, float.MinValue);

                Matrix4x4 toLocal = root.worldToLocalMatrix;

                RectTransform childTransform;
                int validChildren = 0;
                for (int i = 0; i < transform.childCount; i++)
                {
                    childTransform = transform.GetChild(i) as RectTransform;
                    if (isIgnored(childTransform) == false && childTransform.gameObject.activeSelf == true)
                    {
                        validChildren++;
                        childTransform.GetWorldCorners(corners);
                        for (int j = 0; j < 4; j++)
                        {
                            Vector3 v = toLocal.MultiplyPoint3x4(corners[j]);
                            vMin = Vector3.Min(v, vMin);
                            vMax = Vector3.Max(v, vMax);
                        }
                    }
                }

                if (validChildren > 0)
                {
                    Bounds b = new Bounds(vMin, Vector3.zero);
                    b.Encapsulate(vMax);
                    return b;
                }
            }
            
            return new Bounds(Vector3.zero, Vector3.zero);
        }

        protected bool isIgnored(RectTransform t)
        {
            for (int i = 0; i < IgnoreList.Length; i++)
            {
                if(IgnoreList[i] == t)
                {
                    return true;
                }
            }
            return false;
        }
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(ContentSizeFitterForChildren), true)]
    public class ContentSizeFitterForChildrenEditor : Editor
    {
        protected bool showOutPos = true;

        public override void OnInspectorGUI()
        {
            ContentSizeFitterForChildren fitter = (this.target as ContentSizeFitterForChildren);

            base.DrawDefaultInspector();

            if (fitter != null)
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Update Size"))
                {
                    fitter.Refresh();
                }
                GUILayout.EndHorizontal();
            }
        }
    }
#endif
}
