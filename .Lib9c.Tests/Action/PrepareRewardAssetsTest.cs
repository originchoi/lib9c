namespace Lib9c.Tests.Action
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using Libplanet.Action.State;
    using Libplanet.Crypto;
    using Libplanet.Mocks;
    using Libplanet.Types.Assets;
    using Nekoyume;
    using Nekoyume.Action;
    using Nekoyume.Helper;
    using Nekoyume.Model.State;
    using Nekoyume.Module;
    using Xunit;

    public class PrepareRewardAssetsTest
    {
        [Theory]
        [InlineData(true, false, null)]
        [InlineData(true, true, typeof(CurrencyPermissionException))]
        [InlineData(false, false, typeof(PermissionDeniedException))]
        public void Execute(bool admin, bool includeNcg, Type exc)
        {
            var adminAddress = new PrivateKey().Address;
            var poolAddress = new PrivateKey().Address;
            var adminState = new AdminState(adminAddress, 150L);
            var assets = new List<FungibleAssetValue>
            {
                CrystalCalculator.CRYSTAL * 100,
            };
            if (includeNcg)
            {
                var minters = ImmutableHashSet.Create(default(Address));
#pragma warning disable CS0618
                // Use of obsolete method Currency.Legacy(): https://github.com/planetarium/lib9c/discussions/1319
                assets.Add(Currency.Legacy("NCG", 2, minters) * 1);
#pragma warning restore CS0618
            }

            var state = new World(MockUtil.MockModernWorldState)
                .SetLegacyState(Addresses.Admin, adminState.Serialize());

            var action = new PrepareRewardAssets(poolAddress, assets);
            if (exc is null)
            {
                var nextState = action.Execute(
                    new ActionContext
                    {
                        Signer = admin ? adminAddress : poolAddress,
                        BlockIndex = 1,
                        PreviousState = state,
                    });
                foreach (var asset in assets)
                {
                    Assert.Equal(asset, nextState.GetBalance(poolAddress, asset.Currency));
                }
            }
            else
            {
                Assert.Throws(
                    exc,
                    () => action.Execute(
                        new ActionContext
                        {
                            Signer = admin ? adminAddress : poolAddress,
                            BlockIndex = 1,
                            PreviousState = state,
                        }));
            }
        }
    }
}
