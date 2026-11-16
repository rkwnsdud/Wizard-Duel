using UnityEngine;

// 소리의 길이만 바꾸고 음 높이는 그대로 두는 처리(WSOLA).
// 짧은 조각을 절반씩 겹쳐 이어 붙이되, 이음매마다 앞 조각에 이어지는 파형과 가장 닮은 위치를 원본에서 찾아 붙인다.
// 길이를 많이 바꿀수록 소리가 거칠어진다. 한두 배 안쪽에서 쓰는 것을 전제로 한다.
public static class SfxStretch
{
    // 조각 간격(초). 조각 길이는 이 두 배다. 짧을수록 타격음이 또렷하고, 길수록 낮은 소리가 매끄럽다.
    const float HopSeconds = .015f;
    // 이음매를 찾는 범위(초, 앞뒤로).
    const float SearchSeconds = .012f;
    // 이음매를 찾을 때 건너뛰는 간격. 먼저 듬성듬성 찾고 그 둘레를 다시 촘촘히 찾는다.
    const int CoarseStep = 4, SampleStep = 2;

    // samples: 채널이 번갈아 들어 있는 원본. factor: 결과 길이 ÷ 원본 길이.
    public static float[] Stretch(float[] samples, int channels, int rate, float factor)
    {
        int frames = samples.Length / channels;
        int hop = Mathf.Max(16, Mathf.RoundToInt(rate * HopSeconds));
        int window = hop * 2;
        int search = Mathf.Max(CoarseStep, Mathf.RoundToInt(rate * SearchSeconds));
        int outFrames = Mathf.Max(1, Mathf.RoundToInt(frames * factor));
        var output = new float[outFrames * channels];
        var weight = new float[outFrames];

        // 이음매는 채널을 합친 한 줄에서 찾고, 모든 채널을 같은 위치에서 잘라 좌우가 어긋나지 않게 한다.
        var mono = new float[frames];
        for (int i = 0; i < frames; i++)
        for (int c = 0; c < channels; c++)
            mono[i] += samples[i * channels + c];

        // 절반씩 겹치면 합이 1이 되는 창. 양 끝이 0이 아니어서 맨 앞 타격음도 아래 나눗셈으로 원래 크기를 되찾는다.
        var shape = new float[window];
        for (int n = 0; n < window; n++) shape[n] = .5f - .5f * Mathf.Cos(2f * Mathf.PI * (n + .5f) / window);

        int previous = 0;
        for (int k = 0; k * hop < outFrames; k++)
        {
            int start = k == 0 ? 0 : BestStart(mono, previous + hop, Mathf.RoundToInt(k * hop / factor), search, hop);
            for (int n = 0; n < window; n++)
            {
                int to = k * hop + n, from = start + n;
                if (to >= outFrames) break;
                weight[to] += shape[n];
                if (from >= frames) continue;
                for (int c = 0; c < channels; c++) output[to * channels + c] += samples[from * channels + c] * shape[n];
            }
            previous = start;
        }

        for (int i = 0; i < outFrames; i++)
        {
            if (weight[i] <= 0f) continue;
            for (int c = 0; c < channels; c++) output[i * channels + c] /= weight[i];
        }
        return output;
    }

    // wanted 둘레에서, natural에서 시작하는 파형(앞 조각이 그대로 이어졌다면 나왔을 소리)과 가장 닮은 시작 위치를 고른다.
    static int BestStart(float[] mono, int natural, int wanted, int search, int length)
    {
        int last = mono.Length - 1;
        wanted = Mathf.Clamp(wanted, 0, last);
        int best = wanted;
        float bestScore = Score(mono, natural, wanted, length);
        for (int offset = CoarseStep; offset <= search; offset += CoarseStep)
        {
            Try(mono, natural, wanted - offset, length, last, ref best, ref bestScore);
            Try(mono, natural, wanted + offset, length, last, ref best, ref bestScore);
        }
        int coarse = best;
        for (int offset = 1; offset < CoarseStep; offset++)
        {
            Try(mono, natural, coarse - offset, length, last, ref best, ref bestScore);
            Try(mono, natural, coarse + offset, length, last, ref best, ref bestScore);
        }
        return best;
    }

    static void Try(float[] mono, int natural, int candidate, int length, int last, ref int best, ref float bestScore)
    {
        if (candidate < 0 || candidate > last) return;
        float score = Score(mono, natural, candidate, length);
        if (score <= bestScore) return;
        best = candidate;
        bestScore = score;
    }

    // 두 구간이 얼마나 닮았는지. 후보 구간의 크기로 나눠서, 그저 소리가 큰 곳이 뽑히지 않게 한다.
    static float Score(float[] mono, int natural, int candidate, int length)
    {
        float dot = 0f, energy = 1e-9f;
        int end = Mathf.Min(length, mono.Length - Mathf.Max(natural, candidate));
        for (int i = 0; i < end; i += SampleStep)
        {
            float value = mono[candidate + i];
            dot += mono[natural + i] * value;
            energy += value * value;
        }
        return dot / Mathf.Sqrt(energy);
    }
}
