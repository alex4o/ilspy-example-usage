#include <stdint.h>
#include <stdbool.h>
#include <stdio.h>

void main()
{
    int32_t num;
    {
        num = 0;
        while (1)
        {
            num = num + 1;
            if (num >= 5)
            {
                if (num > 10)
                {
                    return;
                }

                printf("%d\n", num);
            }

            continue;
        }
    }
}
