using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Funciones matemáticas para trabajar con el matiz de color (hue) como un
/// valor circular (0 y 1 representan el mismo color, en extremos opuestos de
/// una resta simple). Sin esto, promedios y distancias de hue cerca de los
/// bordes del espectro (rojos) dan resultados incorrectos.
/// </summary>
public static class HueUtils
{
    /// <summary>
    /// Media circular de una lista de valores de hue (0-1), usando la
    /// proyección a vectores unitarios (seno/coseno) en vez de un promedio
    /// aritmético simple, que falla cerca del punto de wrap-around (hue ≈ 0/1).
    /// </summary>
    public static float CircularMean(IReadOnlyList<float> hues)
    {
        if (hues == null || hues.Count == 0)
        {
            return 0f;
        }

        float sumSin = 0f;
        float sumCos = 0f;

        foreach (float hue in hues)
        {
            float angle = hue * 2f * Mathf.PI;
            sumSin += Mathf.Sin(angle);
            sumCos += Mathf.Cos(angle);
        }

        float meanAngle = Mathf.Atan2(sumSin / hues.Count, sumCos / hues.Count);
        float meanHue = meanAngle / (2f * Mathf.PI);

        if (meanHue < 0f)
        {
            meanHue += 1f;
        }

        return meanHue;
    }

    /// <summary>
    /// Distancia circular entre dos valores de hue (0-1). El resultado va de
    /// 0 (mismo color) a 0.5 (colores opuestos en la rueda de color).
    /// </summary>
    public static float CircularDistance(float hueA, float hueB)
    {
        float diff = Mathf.Abs(hueA - hueB);
        return Mathf.Min(diff, 1f - diff);
    }

    /// <summary>
    /// Convierte una distancia circular (0 a 0.5) a un porcentaje de parecido
    /// (100% = mismo color, 0% = colores opuestos).
    /// </summary>
    public static float DistanceToSimilarityPercent(float circularDistance)
    {
        float normalized = Mathf.Clamp01(circularDistance / 0.5f);
        return (1f - normalized) * 100f;
    }
}