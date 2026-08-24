using UnityEngine;

public class SkyboxGradientBreathing : MonoBehaviour
{
    public Material skyboxMaterial;
    public float speed = 0.5f;      // 애니메이션 속도
    public float amplitude = 0.5f;  // Y 방향 변화 크기

    private Vector3 baseDirection;

    void Start()
    {
        if (skyboxMaterial != null)
        {
            baseDirection = skyboxMaterial.GetVector("_Direction");
        }
    }

    void Update()
    {
        if (skyboxMaterial == null) return;

        // 1. Top–Bottom만 보이도록 MiddleColor를 항상 보간값으로 강제
        Color top = skyboxMaterial.GetColor("_TopColor");
        Color bottom = skyboxMaterial.GetColor("_BottomColor");
        Color middle = Color.Lerp(bottom, top, 0.5f);
        skyboxMaterial.SetColor("_MiddleColor", middle);

        // 2. Y 방향 일렁임 (숨쉬기 효과)
        float wave = Mathf.Sin(Time.time * speed) * amplitude;
        Vector3 animatedDirection = new Vector3(
            baseDirection.x,
            wave,   // Y만 -amplitude ~ +amplitude 범위에서 진동
            baseDirection.z
        );

        skyboxMaterial.SetVector("_Direction", animatedDirection);
    }
}
