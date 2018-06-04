using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Cloud
{
    public class DynamoDBForeignKeyAttribute : Attribute
    {
        public readonly string TableName;
        public DynamoDBForeignKeyAttribute(string tableName)
        {
            TableName = tableName;
        }
    }

    public class DynamoDBCapacityAttribute : Attribute
    {
        public readonly int FixedCapacity;
        public readonly int MinCapacity;
        public readonly int MaxCapacity;
        public DynamoDBCapacityAttribute(int minCapacity, int maxCapacity = -1)
        {
            if (maxCapacity >= 0)
            {
                MinCapacity = minCapacity;
                MaxCapacity = maxCapacity;
                FixedCapacity = -1;
            }
            else
            {
                MinCapacity = -1;
                MaxCapacity = -1;
                FixedCapacity = minCapacity;
            }
        }

        public bool Fixed
        {
            get
            {
                return FixedCapacity >= 0;
            }
        }
    }

    public class DynamoDBReadCapacityAttribute : DynamoDBCapacityAttribute
    {
        public DynamoDBReadCapacityAttribute(int minCapacity, int maxCapacity = -1) : base(minCapacity, maxCapacity)
        {
        }
    }
    public class DynamoDBWriteCapacityAttribute : DynamoDBCapacityAttribute
    {
        public DynamoDBWriteCapacityAttribute(int minCapacity, int maxCapacity = -1) : base(minCapacity, maxCapacity)
        {
        }
    }
}
