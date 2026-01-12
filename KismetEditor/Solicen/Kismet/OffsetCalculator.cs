using System.Linq;

namespace Solicen.Kismet
{
    internal class OffsetCalculator
    {

        public static int GetOffset(LObject[] kismets, int MAGIC)
        {
            var _kismets = kismets.ToList();
            // Ищем инструкцию перехода, у которой значение смещения равно нашему "магическому числу"
            var item = kismets.FirstOrDefault(x => x.Instruction == "Jump" && x.Offset == MAGIC);
            if (item != null)
            {
                int index = _kismets.IndexOf(item);
                if (index == -1) return 0; // Не должно произойти, но на всякий случай

                switch (MAGIC)
                {
                    case 200519:
                        if (index + 2 >= kismets.Length) return -1; // Выход за пределы массива
                        return kismets[index + 2].StatementIndex;
                    case 60519:
                        if (index - 1 < 0) return -1; // Выход за пределы массива
                        return kismets[index - 1].StatementIndex;
                    default:
                        return -1;

                }
            }
            return 0;
        }
    }
}