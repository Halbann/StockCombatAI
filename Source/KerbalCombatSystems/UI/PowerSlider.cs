using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections;
using System.Reflection;

using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.Events;
using TMPro;

namespace KerbalCombatSystems.UI
{
    public class UI_MinMaxPow : UI_MinMaxRange
    {
        public static string UIControlName = "MinMaxPow";
    }

    // This is a re-implementation of UIPartActionMinMaxRange that uses a power law slider instead of a linear slider.
    // It's used on the weapon controller to compress the min (a few hundred metres) and max (many kilometres) weapon ranges into a usable space.

    [UI_MinMaxPow]
    public class UIPartActionMinMaxPow : UIPartActionFieldItem
    {
        public GameObject slidersContainer;
        public TextMeshProUGUI fieldName;
        public TextMeshProUGUI fieldAmount;
        public GameObject numericContainer;
        public TextMeshProUGUI fieldNameNumeric;
        public TMP_InputField inputFieldMin;
        public TMP_InputField inputFieldMax;
        public DoubleSlider slider;
        private Vector2 fieldValue = Vector2.zero;
        private float lerpedValueMin;
        private float moddedValueMin;
        private float lerpedValueMax;
        private float moddedValueMax;
        private bool handlingChange;

        protected UI_MinMaxPow ProgBarControl
        {
            get
            {
                return (UI_MinMaxPow)control;
            }
        }

        public float InterpolatePow(float a, float b, float t)
        {
            float lerped = Mathf.Lerp(Mathf.Sqrt(a), Mathf.Sqrt(b), t);
            return Mathf.Round(lerped * lerped);
        }

        public float InterpolatePowInverse(float a, float b, float value)
        {
            float lerped = Mathf.InverseLerp(Mathf.Sqrt(a), Mathf.Sqrt(b), Mathf.Sqrt(value));
            return lerped;
        }

        public override void Setup(UIPartActionWindow window, Part part, PartModule partModule, UI_Scene scene, UI_Control control, BaseField field)
        {
            base.Setup(window, part, partModule, scene, control, field);

            SetSliderValue(GetFieldValue());
            DoubleSlider doubleSlider = slider;
            doubleSlider.onValueChanged = (DoubleSlider.OnValueChanged)Delegate.Combine(doubleSlider.onValueChanged, new DoubleSlider.OnValueChanged(OnValueChanged));

            inputFieldMin.onEndEdit.AddListener(new UnityAction<string>(SetNumericMinValue));
            inputFieldMax.onEndEdit.AddListener(new UnityAction<string>(SetNumericMaxValue));
            inputFieldMin.onSelect.AddListener(new UnityAction<string>(AddInputFieldLock));
            inputFieldMax.onSelect.AddListener(new UnityAction<string>(AddInputFieldLock));

            GameEvents.onPartActionNumericSlider.Add(new EventData<bool>.OnEvent(ToggleNumericSlider));
            ToggleNumericSlider(GameSettings.PAW_NUMERIC_SLIDERS);
        }

        internal void OnDestroy()
        {
            GameEvents.onPartActionNumericSlider.Remove(new EventData<bool>.OnEvent(ToggleNumericSlider));
        }

        // This function is called when the slider moves, either by the player or from UpdateItem.
        private void OnValueChanged(float minValue, float maxValue)
        {
            if (handlingChange)
                return;

            handlingChange = true;

            ControlTypes controlType = control != null && control.requireFullControl ?
                ControlTypes.TWEAKABLES_FULLONLY : ControlTypes.TWEAKABLES_ANYCONTROL;

            if (InputLockManager.IsUnlocked(controlType))
            {
                lerpedValueMin = InterpolatePow(ProgBarControl.minValueX, ProgBarControl.maxValueY, minValue);
                moddedValueMin = lerpedValueMin % ProgBarControl.stepIncrement;

                lerpedValueMax = InterpolatePow(ProgBarControl.minValueX, ProgBarControl.maxValueY, maxValue);
                moddedValueMax = lerpedValueMax % ProgBarControl.stepIncrement;


                // Min.

                float x = fieldValue.x;

                if (moddedValueMin != 0f)
                {
                    // Quantize min to the nearest step increment.
                    if (moddedValueMin < ProgBarControl.stepIncrement * 0.5f)
                    {
                        fieldValue.x = lerpedValueMin - moddedValueMin;
                    }
                    else
                    {
                        fieldValue.x = lerpedValueMin + (ProgBarControl.stepIncrement - moddedValueMin);
                    }
                }
                else
                {
                    fieldValue.x = lerpedValueMin;
                }


                // Max.

                float y = fieldValue.y;

                if (moddedValueMax != 0f)
                {
                    // Quantize max to the nearest step increment.
                    if (moddedValueMax < ProgBarControl.stepIncrement * 0.5f)
                    {
                        fieldValue.y = lerpedValueMax - moddedValueMax;
                    }
                    else
                    {
                        fieldValue.y = lerpedValueMax + (ProgBarControl.stepIncrement - moddedValueMax);
                    }
                }
                else
                {
                    fieldValue.y = lerpedValueMax;
                }


                // Only set the field value if it's different by at least one step increment.
                if (Mathf.Abs(fieldValue.y - y) > ProgBarControl.stepIncrement * 0.98f
                    || Mathf.Abs(fieldValue.x - x) > ProgBarControl.stepIncrement * 0.98f)
                    SetFieldValue(fieldValue);
            }

            handlingChange = false;
        }

        private Vector2 GetFieldValue()
        {
            return field.GetValue<Vector2>(field.host);
        }

        private void SetSliderValue(Vector2 rawValue)
        {
            slider.sliderMin.value = InterpolatePowInverse(ProgBarControl.minValueX, ProgBarControl.maxValueY, rawValue.x);
            slider.sliderMax.value = InterpolatePowInverse(ProgBarControl.minValueX, ProgBarControl.maxValueY, rawValue.y);
        }

        public override void UpdateItem()
        {
            fieldValue = GetFieldValue();
            fieldName.text = field.guiName;

            // Dynamically change the units.

            string minText = fieldValue.x < 1000 ? $"{fieldValue.x:N0} m" : $"{fieldValue.x / 1000:0.0} km";
            string maxText = fieldValue.y < 1000 ? $"{fieldValue.y:N0} m" : $"{fieldValue.y / 1000:0.0} km";

            fieldAmount.text = $"{minText}, {maxText}";


            // Numeric fields.

            fieldNameNumeric.text = field.guiName;

            if (!inputFieldMin.isFocused)
            {
                inputFieldMin.text = KSPUtil.LocalizeNumber(fieldValue.x, field.guiFormat);
            }
            if (!inputFieldMax.isFocused)
            {
                inputFieldMax.text = KSPUtil.LocalizeNumber(fieldValue.y, field.guiFormat);
            }


            // Update slider.
            SetSliderValue(fieldValue);
        }

        private void SetNumericMinValue(string input)
        {
            if (float.TryParse(input, out float num))
            {
                num = Mathf.Clamp(num, ProgBarControl.minValueX, fieldValue.y - ProgBarControl.stepIncrement);
                fieldValue.x = num;
                SetFieldValue(fieldValue);
                inputFieldMin.text = KSPUtil.LocalizeNumber(fieldValue.x, field.guiFormat);
            }

            RemoveInputfieldLock();
        }

        private void SetNumericMaxValue(string input)
        {
            if (float.TryParse(input, out float num))
            {
                num = Mathf.Clamp(num, fieldValue.x + ProgBarControl.stepIncrement, ProgBarControl.maxValueY);
                fieldValue.y = num;
                SetFieldValue(fieldValue);
                inputFieldMax.text = KSPUtil.LocalizeNumber(fieldValue.y, field.guiFormat);
            }

            RemoveInputfieldLock();
        }

        private void ToggleNumericSlider(bool numeric)
        {
            slidersContainer.SetActive(!numeric);
            numericContainer.SetActive(numeric);
        }

        // This function duplicates the prefab for the UIPartActionMinMaxRange setup
        // but using the UIPartActionMinMaxPow component, and manually populates
        // the public references to objects in the prefab.
        public static UIPartActionMinMaxPow CreateTemplate()
        {
            // Create the control
            GameObject gameObject = new GameObject("FieldMinMaxPow", typeof(UIPartActionMinMaxPow));
            UIPartActionMinMaxPow partActionMinMaxPow = gameObject.GetComponent<UIPartActionMinMaxPow>();
            gameObject.SetActive(false);

            // Find the template for MinMaxRange
            UIPartActionMinMaxRange partActionMinMaxRange = (UIPartActionMinMaxRange)UIPartActionController.Instance.fieldPrefabs.Find(cls => cls.GetType() == typeof(UIPartActionMinMaxRange));

            // Copy UI elements
            RectTransform rtc = gameObject.AddComponent<RectTransform>();
            RectTransform rt = partActionMinMaxRange.transform as RectTransform;
            rtc.offsetMin = rt.offsetMin;
            rtc.offsetMax = rt.offsetMax;
            rtc.anchorMin = rt.anchorMin;
            rtc.anchorMax = rt.anchorMax;
            LayoutElement lec = gameObject.AddComponent<LayoutElement>();
            LayoutElement le = partActionMinMaxRange.GetComponent<LayoutElement>();
            lec.flexibleHeight = le.flexibleHeight;
            lec.flexibleWidth = le.flexibleWidth;
            lec.minHeight = le.minHeight;
            lec.minWidth = le.minWidth;
            lec.preferredHeight = le.preferredHeight;
            lec.preferredWidth = le.preferredWidth;
            lec.layoutPriority = le.layoutPriority;

            // Copy control elements
            Dictionary<GameObject, GameObject> list = new Dictionary<GameObject, GameObject>();
            InstantiateRecursive(partActionMinMaxRange.gameObject, gameObject.transform, ref list);

            //slidersContainer
            list.TryGetValue(partActionMinMaxRange.slidersContainer, out GameObject slidersContainer);
            partActionMinMaxPow.slidersContainer = slidersContainer;

            //fieldName
            list.TryGetValue(partActionMinMaxRange.fieldName.gameObject, out GameObject fieldNameGO);
            partActionMinMaxPow.fieldName = fieldNameGO.GetComponent<TextMeshProUGUI>();

            //fieldAmount
            list.TryGetValue(partActionMinMaxRange.fieldAmount.gameObject, out GameObject fieldAmountGO);
            partActionMinMaxPow.fieldAmount = fieldAmountGO.GetComponent<TextMeshProUGUI>();

            //numericContainer
            list.TryGetValue(partActionMinMaxRange.numericContainer, out GameObject numericContainer);
            partActionMinMaxPow.numericContainer = numericContainer;

            //fieldNameNumeric
            list.TryGetValue(partActionMinMaxRange.fieldNameNumeric.gameObject, out GameObject fieldNameNumericGO);
            partActionMinMaxPow.fieldNameNumeric = fieldNameNumericGO.GetComponent<TextMeshProUGUI>();

            //inputFieldMin
            list.TryGetValue(partActionMinMaxRange.inputFieldMin.gameObject, out GameObject inputFieldMinGO);
            partActionMinMaxPow.inputFieldMin = inputFieldMinGO.GetComponent<TMP_InputField>();

            //inputFieldMax
            list.TryGetValue(partActionMinMaxRange.inputFieldMax.gameObject, out GameObject inputFieldMaxGO);
            partActionMinMaxPow.inputFieldMax = inputFieldMaxGO.GetComponent<TMP_InputField>();

            //slider
            list.TryGetValue(partActionMinMaxRange.slider.gameObject, out GameObject sliderGO);
            partActionMinMaxPow.slider = sliderGO.GetComponent<DoubleSlider>();

            partActionMinMaxPow = EditTemplate(partActionMinMaxPow);

            return partActionMinMaxPow;
        }

        public static UIPartActionMinMaxPow EditTemplate(UIPartActionMinMaxPow partActionMinMaxPow)
        {
            // Custom changes to the MinMaxPow prefab.

            var fieldAmount = partActionMinMaxPow.fieldAmount;
            fieldAmount.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 999);
            fieldAmount.fontSizeMin = fieldAmount.fontSizeMax;

            return partActionMinMaxPow;
        }

        public static void InstantiateRecursive(GameObject go, Transform trfp, ref Dictionary<GameObject, GameObject> list)
        {
            for (int i = 0; i < go.transform.childCount; i++)
            {
                GameObject goc = Instantiate(go.transform.GetChild(i).gameObject);
                goc.transform.parent = trfp;
                goc.transform.localPosition = go.transform.GetChild(i).localPosition;
                if ((goc.transform is RectTransform) && (go.transform.GetChild(i) is RectTransform))
                {
                    RectTransform rtc = goc.transform as RectTransform;
                    RectTransform rt = go.transform.GetChild(i) as RectTransform;

                    rtc.offsetMax = rt.offsetMax;
                    rtc.offsetMin = rt.offsetMin;
                }
                list.Add(go.transform.GetChild(i).gameObject, goc);
                InstantiateRecursive2(go.transform.GetChild(i).gameObject, goc, ref list);
            }
        }

        public static void InstantiateRecursive2(GameObject go, GameObject goc, ref Dictionary<GameObject, GameObject> list)
        {
            for (int i = 0; i < go.transform.childCount; i++)
            {
                list.Add(go.transform.GetChild(i).gameObject, goc.transform.GetChild(i).gameObject);
                InstantiateRecursive2(go.transform.GetChild(i).gameObject, goc.transform.GetChild(i).gameObject, ref list);
            }
        }
    }


    // This class runs once to register the new min max power slider with the part action UI system.
    // So we can use it as if it were a stock UI component.

    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    internal class UIPartActionFloatLogRangeRegistration : MonoBehaviour
    {
        private static bool loaded = false;
        private static bool isRunning = false;
        private Coroutine register = null;

        public void Start()
        {
            if (loaded)
            {
                Destroy(gameObject);
                return;
            }
            loaded = true;
            DontDestroyOnLoad(gameObject);
            SceneManager.sceneLoaded += OnLevelFinishedLoading;
        }

        public void OnLevelFinishedLoading(Scene scene, LoadSceneMode mode)
        {
            if (isRunning && register != null)
                StopCoroutine(register);

            if (!(HighLogic.LoadedSceneIsEditor || HighLogic.LoadedSceneIsFlight)) return;

            isRunning = true;
            register = StartCoroutine(Register());
        }

        internal IEnumerator Register()
        {
            UIPartActionController controller;
            while ((controller = UIPartActionController.Instance) is null) yield return null;

            FieldInfo typesField = (from fld in controller.GetType().GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                                    where fld.FieldType == typeof(List<Type>)
                                    select fld).First();

            List<Type> fieldPrefabTypes;
            while ((fieldPrefabTypes = (List<Type>)typesField.GetValue(controller)) == null
                || fieldPrefabTypes.Count == 0
                || !UIPartActionController.Instance.fieldPrefabs.Find(cls => cls.GetType() == typeof(UIPartActionFloatRange)))
                yield return false;

            // Register prefabs
            controller.fieldPrefabs.Add(UIPartActionMinMaxPow.CreateTemplate());
            fieldPrefabTypes.Add(typeof(UI_MinMaxPow));

            isRunning = false;
        }
    }
}
