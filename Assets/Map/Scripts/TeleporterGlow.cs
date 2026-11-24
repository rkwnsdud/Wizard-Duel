using System;
using UnityEngine;

// 텔레포트 타일의 하이라이트 빛 설정. 값은 웹 시안(이슈 #131) A안에서 고른 값이다.
[Serializable]
public sealed class TeleporterGlowSettings
{
    [Tooltip("쓸 수 있을 때의 빛 세기(HDR 배수). 1보다 작아 블룸 없이 은은하게 빛난다.")]
    [Min(0f)] public float readyIntensity = 0.6f;
    [Tooltip("쓸 수 있을 때 빛이 오르내리는 폭. 빛 세기에 (1 ± 이 값)을 곱한다.")]
    [Range(0f, 1f)] public float pulse = 0.15f;
    [Tooltip("빛이 한 번 오르내리는 시간(초).")]
    [Min(0.1f)] public float pulsePeriod = 2.5f;
    [Tooltip("밟아서 이동할 때 번쩍이는 빛 세기. 1보다 커서 블룸이 생긴다.")]
    [Min(0f)] public float flashIntensity = 6.5f;
    [Tooltip("번쩍임이 쿨타임 빛까지 줄어드는 시간(초).")]
    [Min(0.01f)] public float flashDuration = 0.6f;
    [Tooltip("쿨타임 동안의 빛 세기. 거의 꺼져 어둡다.")]
    [Min(0f)] public float cooldownIntensity = 0.04f;
    [Tooltip("쿨타임 동안 하이라이트 색에 곱하는 밝기. 1이면 쓸 수 있을 때와 같은 색이다.")]
    [Range(0f, 1f)] public float cooldownTint = 0.3f;
    [Tooltip("쿨타임이 끝난 뒤 다시 쓸 수 있는 빛으로 돌아오는 시간(초).")]
    [Min(0f)] public float recoverTime = 0.4f;
    [Tooltip("쓸 수 있을 때 안쪽 무늬가 도는 빠르기(도/초). 쿨타임 동안은 멈춘다.")]
    public float spinSpeed = 15f;
}

// 텔레포트 타일의 빛 세기를 시각으로 계산한다. 시각은 모두 같은 시계(보통 Time.time)다.
public static class TeleporterGlow
{
    // level은 하이라이트 머티리얼의 발광 세기, ready는 쓸 수 있는 정도(0이면 쿨타임, 1이면 쓸 수 있음)다.
    // flashStart는 마지막으로 발동한 시각, cooldownEnd는 다시 쓸 수 있는 시각이다. 발동한 적이 없으면 둘 다 음의 무한대다.
    public static void Evaluate(TeleporterGlowSettings settings, float now, float flashStart, float cooldownEnd,
        out float level, out float ready)
    {
        float cool = settings.cooldownIntensity;
        if (now < cooldownEnd)
        {
            ready = 0f;
            level = cool;
            float f = (now - flashStart) / Mathf.Max(0.0001f, settings.flashDuration);
            if (f >= 0f && f < 1f)
            {
                float k = (1f - f) * (1f - f);
                level = cool + (settings.flashIntensity - cool) * k;
                ready = k;
            }
            return;
        }
        float readyLevel = settings.readyIntensity *
            (1f + settings.pulse * Mathf.Sin(now * Mathf.PI * 2f / Mathf.Max(0.1f, settings.pulsePeriod)));
        ready = settings.recoverTime > 0f ? Mathf.Min(1f, (now - cooldownEnd) / settings.recoverTime) : 1f;
        level = cool + (readyLevel - cool) * ready;
    }

    // ready에 맞춘 하이라이트 바탕색 밝기. 쿨타임 동안은 cooldownTint만큼 어둡다.
    public static float Tint(TeleporterGlowSettings settings, float ready) =>
        settings.cooldownTint + (1f - settings.cooldownTint) * ready;
}
