namespace DefconSaver;

public sealed class City
{
    public string Name;
    public float Lat, Lon;
    public float Population;      // millions, at the start of the scenario
    public float Alive;           // millions still alive
    public int Territory;
    public float Flash;           // 0..1, decays after a hit
    public int Hits;

    public float Px, Py, Pz;      // unit-sphere position

    public bool Dead => Alive <= 0.05f;
}

public static class CityData
{
    // name, lat, lon, population in millions
    private static readonly object[] Raw =
    {
        // --- North America -------------------------------------------------
        "NEW YORK", 40.71f, -74.01f, 18.8f,
        "LOS ANGELES", 34.05f, -118.24f, 12.5f,
        "CHICAGO", 41.88f, -87.63f, 8.9f,
        "MEXICO CITY", 19.43f, -99.13f, 21.8f,
        "TORONTO", 43.65f, -79.38f, 6.2f,
        "HOUSTON", 29.76f, -95.37f, 6.3f,
        "WASHINGTON", 38.91f, -77.04f, 5.5f,
        "SAN FRANCISCO", 37.77f, -122.42f, 4.7f,
        "MIAMI", 25.76f, -80.19f, 6.1f,
        "SEATTLE", 47.61f, -122.33f, 4.0f,
        "DENVER", 39.74f, -104.99f, 2.9f,
        "MONTREAL", 45.50f, -73.57f, 4.2f,
        "VANCOUVER", 49.28f, -123.12f, 2.6f,
        "HAVANA", 23.11f, -82.37f, 2.1f,
        "GUATEMALA CITY", 14.63f, -90.51f, 3.0f,
        "ATLANTA", 33.75f, -84.39f, 6.0f,
        "DALLAS", 32.78f, -96.80f, 7.6f,
        "PHOENIX", 33.45f, -112.07f, 4.9f,
        "BOSTON", 42.36f, -71.06f, 4.9f,
        "ANCHORAGE", 61.22f, -149.90f, 0.3f,

        // --- South America -------------------------------------------------
        "SAO PAULO", -23.55f, -46.63f, 22.4f,
        "BUENOS AIRES", -34.60f, -58.38f, 15.3f,
        "RIO DE JANEIRO", -22.91f, -43.17f, 13.5f,
        "LIMA", -12.05f, -77.04f, 10.9f,
        "BOGOTA", 4.71f, -74.07f, 11.0f,
        "SANTIAGO", -33.45f, -70.67f, 6.8f,
        "CARACAS", 10.48f, -66.90f, 3.0f,
        "BRASILIA", -15.79f, -47.88f, 4.8f,
        "MEDELLIN", 6.24f, -75.58f, 4.1f,
        "MONTEVIDEO", -34.90f, -56.16f, 1.8f,
        "LA PAZ", -16.50f, -68.15f, 2.0f,
        "QUITO", -0.18f, -78.47f, 2.0f,
        "ASUNCION", -25.26f, -57.58f, 3.2f,
        "RECIFE", -8.05f, -34.88f, 4.1f,
        "BELO HORIZONTE", -19.92f, -43.94f, 6.0f,

        // --- Europe --------------------------------------------------------
        "LONDON", 51.51f, -0.13f, 9.6f,
        "PARIS", 48.86f, 2.35f, 11.1f,
        "ISTANBUL", 41.01f, 28.98f, 15.6f,
        "MADRID", 40.42f, -3.70f, 6.7f,
        "BERLIN", 52.52f, 13.40f, 3.7f,
        "ROME", 41.90f, 12.50f, 4.3f,
        "BARCELONA", 41.39f, 2.17f, 5.6f,
        "VIENNA", 48.21f, 16.37f, 2.0f,
        "AMSTERDAM", 52.37f, 4.90f, 2.5f,
        "HAMBURG", 53.55f, 9.99f, 1.9f,
        "MUNICH", 48.14f, 11.58f, 1.6f,
        "MILAN", 45.46f, 9.19f, 3.1f,
        "NAPLES", 40.85f, 14.27f, 2.2f,
        "ATHENS", 37.98f, 23.73f, 3.2f,
        "LISBON", 38.72f, -9.14f, 2.9f,
        "DUBLIN", 53.35f, -6.26f, 1.2f,
        "STOCKHOLM", 59.33f, 18.07f, 1.6f,
        "OSLO", 59.91f, 10.75f, 1.1f,
        "COPENHAGEN", 55.68f, 12.57f, 1.4f,
        "HELSINKI", 60.17f, 24.94f, 1.3f,
        "WARSAW", 52.23f, 21.01f, 1.8f,
        "PRAGUE", 50.08f, 14.44f, 1.3f,
        "BUDAPEST", 47.50f, 19.04f, 1.8f,
        "BUCHAREST", 44.43f, 26.10f, 1.8f,
        "BRUSSELS", 50.85f, 4.35f, 2.1f,
        "MANCHESTER", 53.48f, -2.24f, 2.7f,
        "GLASGOW", 55.86f, -4.25f, 1.7f,
        "ZURICH", 47.37f, 8.54f, 1.4f,
        "ANKARA", 39.93f, 32.86f, 5.7f,
        "REYKJAVIK", 64.15f, -21.94f, 0.2f,

        // --- Russia --------------------------------------------------------
        "MOSCOW", 55.76f, 37.62f, 12.6f,
        "ST PETERSBURG", 59.93f, 30.34f, 5.4f,
        "KYIV", 50.45f, 30.52f, 3.0f,
        "NOVOSIBIRSK", 55.03f, 82.92f, 1.6f,
        "YEKATERINBURG", 56.84f, 60.65f, 1.5f,
        "MINSK", 53.90f, 27.57f, 2.0f,
        "NIZHNY NOVGOROD", 56.33f, 44.00f, 1.2f,
        "KAZAN", 55.80f, 49.11f, 1.3f,
        "SAMARA", 53.20f, 50.15f, 1.2f,
        "OMSK", 54.99f, 73.37f, 1.2f,
        "CHELYABINSK", 55.16f, 61.40f, 1.2f,
        "PERM", 58.01f, 56.25f, 1.0f,
        "UFA", 54.74f, 55.97f, 1.1f,
        "KRASNOYARSK", 56.01f, 92.87f, 1.1f,
        "IRKUTSK", 52.29f, 104.30f, 0.6f,
        "KHARKIV", 49.99f, 36.23f, 1.4f,
        "ODESA", 46.48f, 30.73f, 1.0f,
        "MURMANSK", 68.97f, 33.08f, 0.3f,
        "YAKUTSK", 62.03f, 129.73f, 0.3f,

        // --- Africa and the Middle East -------------------------------------
        "CAIRO", 30.04f, 31.24f, 21.3f,
        "LAGOS", 6.52f, 3.38f, 15.4f,
        "KINSHASA", -4.44f, 15.27f, 15.6f,
        "JOHANNESBURG", -26.20f, 28.05f, 6.0f,
        "NAIROBI", -1.29f, 36.82f, 5.1f,
        "CASABLANCA", 33.57f, -7.59f, 3.8f,
        "ADDIS ABABA", 9.03f, 38.74f, 5.2f,
        "ALGIERS", 36.75f, 3.06f, 2.9f,
        "KHARTOUM", 15.50f, 32.56f, 6.0f,
        "DAR ES SALAAM", -6.79f, 39.21f, 7.4f,
        "CAPE TOWN", -33.92f, 18.42f, 4.8f,
        "ACCRA", 5.60f, -0.19f, 2.6f,
        "ABIDJAN", 5.36f, -4.01f, 5.6f,
        "LUANDA", -8.84f, 13.23f, 8.9f,
        "DAKAR", 14.72f, -17.47f, 3.2f,
        "TRIPOLI", 32.89f, 13.19f, 1.2f,
        "MOGADISHU", 2.05f, 45.32f, 2.6f,
        "KAMPALA", 0.35f, 32.58f, 3.7f,
        "TEHRAN", 35.69f, 51.39f, 9.4f,
        "BAGHDAD", 33.31f, 44.36f, 7.5f,
        "RIYADH", 24.71f, 46.68f, 7.7f,
        "TEL AVIV", 32.08f, 34.78f, 4.3f,
        "DUBAI", 25.20f, 55.27f, 3.5f,
        "HARARE", -17.83f, 31.05f, 1.6f,

        // --- Asia and Oceania ------------------------------------------------
        "TOKYO", 35.69f, 139.69f, 37.2f,
        "DELHI", 28.61f, 77.21f, 32.9f,
        "SHANGHAI", 31.23f, 121.47f, 29.2f,
        "DHAKA", 23.81f, 90.41f, 22.5f,
        "BEIJING", 39.90f, 116.41f, 21.8f,
        "MUMBAI", 19.08f, 72.88f, 21.3f,
        "OSAKA", 34.69f, 135.50f, 19.0f,
        "CHONGQING", 29.56f, 106.55f, 17.3f,
        "KARACHI", 24.86f, 67.01f, 16.8f,
        "CHENGDU", 30.57f, 104.07f, 16.3f,
        "KOLKATA", 22.57f, 88.36f, 15.1f,
        "MANILA", 14.60f, 120.98f, 14.7f,
        "WUHAN", 30.59f, 114.31f, 13.9f,
        "GUANGZHOU", 23.13f, 113.26f, 13.6f,
        "BENGALURU", 12.97f, 77.59f, 13.2f,
        "LAHORE", 31.55f, 74.34f, 13.1f,
        "SHENZHEN", 22.54f, 114.06f, 12.6f,
        "CHENNAI", 13.08f, 80.27f, 11.5f,
        "BANGKOK", 13.76f, 100.50f, 10.7f,
        "JAKARTA", -6.21f, 106.85f, 10.6f,
        "SEOUL", 37.57f, 126.98f, 9.9f,
        "HO CHI MINH CITY", 10.82f, 106.63f, 9.3f,
        "HANOI", 21.03f, 105.85f, 8.5f,
        "KUALA LUMPUR", 3.14f, 101.69f, 8.4f,
        "HONG KONG", 22.32f, 114.17f, 7.5f,
        "TAIPEI", 25.03f, 121.57f, 7.0f,
        "SINGAPORE", 1.35f, 103.82f, 5.9f,
        "YANGON", 16.87f, 96.20f, 5.4f,
        "SYDNEY", -33.87f, 151.21f, 5.3f,
        "MELBOURNE", -37.81f, 144.96f, 5.1f,
        "PYONGYANG", 39.02f, 125.74f, 3.0f,
        "PERTH", -31.95f, 115.86f, 2.1f,
        "AUCKLAND", -36.85f, 174.76f, 1.7f,
        "ISLAMABAD", 33.68f, 73.05f, 1.2f,
        "VLADIVOSTOK", 43.12f, 131.89f, 0.6f,
    };

    public static City[] Build()
    {
        int n = Raw.Length / 4;
        var list = new List<City>(n);
        for (int i = 0; i < n; i++)
        {
            var c = new City
            {
                Name = (string)Raw[i * 4],
                Lat = (float)Raw[i * 4 + 1],
                Lon = (float)Raw[i * 4 + 2],
                Population = (float)Raw[i * 4 + 3],
            };
            c.Alive = c.Population;
            c.Territory = Territories.At(c.Lat, c.Lon);
            Geo.ToVec(c.Lat, c.Lon, out c.Px, out c.Py, out c.Pz);
            if (c.Territory >= 0) list.Add(c);
        }
        return list.ToArray();
    }
}
