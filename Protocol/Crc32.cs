namespace Srtp.Protocol;

public static class Crc32
{
    // Polinomio refletido do CRC32 IEEE, o mesmo usado em Ethernet, ZIP e PNG.
    private const uint Polynomial = 0xEDB88320u;
    private static readonly uint[] Table = BuildTable();

    public static uint Compute(ReadOnlySpan<byte> data)
    {
        // Inicializacao e complemento final seguem a convencao classica do CRC32 IEEE.
        // Se qualquer bit do cabecalho ou payload mudar no caminho, a chance de o CRC bater e muito baixa.
        uint crc = 0xFFFFFFFFu;

        foreach (byte b in data)
        {
            uint index = (crc ^ b) & 0xFFu;
            crc = (crc >> 8) ^ Table[index];
        }

        return ~crc;
    }

    private static uint[] BuildTable()
    {
        // Tabela pre-computada para acelerar o calculo: cada byte processado vira uma consulta.
        uint[] table = new uint[256];
        for (uint i = 0; i < table.Length; i++)
        {
            uint value = i;
            for (int bit = 0; bit < 8; bit++)
            {
                value = (value & 1u) != 0 ? (value >> 1) ^ Polynomial : value >> 1;
            }

            table[i] = value;
        }

        return table;
    }
}
