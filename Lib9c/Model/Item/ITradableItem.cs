﻿using System;

namespace Nekoyume.Model.Item
{
    public interface ITradableItem: IItem
    {
        Guid TradeId { get; }
    }
}
