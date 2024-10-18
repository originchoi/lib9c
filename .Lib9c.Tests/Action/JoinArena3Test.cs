namespace Lib9c.Tests.Action
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Bencodex.Types;
    using Libplanet.Action;
    using Libplanet.Action.State;
    using Libplanet.Crypto;
    using Libplanet.Mocks;
    using Libplanet.Types.Assets;
    using Nekoyume;
    using Nekoyume.Action;
    using Nekoyume.Arena;
    using Nekoyume.Model;
    using Nekoyume.Model.Arena;
    using Nekoyume.Model.EnumType;
    using Nekoyume.Model.Item;
    using Nekoyume.Model.Rune;
    using Nekoyume.Model.State;
    using Nekoyume.Module;
    using Nekoyume.TableData;
    using Serilog;
    using Xunit;
    using Xunit.Abstractions;
    using static Lib9c.SerializeKeys;

    public class JoinArena3Test
    {
        private readonly Dictionary<string, string> _sheets;
        private readonly TableSheets _tableSheets;
        private readonly Address _signer;
        private readonly Address _signer2;
        private readonly Address _avatarAddress;
        private readonly Address _avatar2Address;
        private readonly IRandom _random;
        private readonly Currency _currency;
        private IWorld _state;

        public JoinArena3Test(ITestOutputHelper outputHelper)
        {
            _random = new TestRandom();
            _sheets = TableSheetsImporter.ImportSheets();
            _tableSheets = new TableSheets(_sheets);

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.TestOutput(outputHelper)
                .CreateLogger();

            _state = new World(MockUtil.MockModernWorldState);

            _signer = new PrivateKey().Address;
            _avatarAddress = _signer.Derive("avatar");
            var sheets = TableSheetsImporter.ImportSheets();
            var tableSheets = new TableSheets(sheets);
            var rankingMapAddress = new PrivateKey().Address;
            var agentState = new AgentState(_signer);
            var gameConfigState = new GameConfigState(_sheets[nameof(GameConfigSheet)]);
            var avatarState = AvatarState.Create(
                _avatarAddress,
                _signer,
                0,
                tableSheets.GetAvatarSheets(),
                rankingMapAddress);
            avatarState.worldInformation = new WorldInformation(
                0,
                tableSheets.WorldSheet,
                GameConfig.RequireClearedStageLevel.ActionsInRankingBoard);

            agentState.avatarAddresses[0] = _avatarAddress;
            avatarState.level = GameConfig.RequireClearedStageLevel.ActionsInRankingBoard;

            _signer2 = new PrivateKey().Address;
            _avatar2Address = _signer2.Derive("avatar");
            var agent2State = new AgentState(_signer2);

            var avatar2State = AvatarState.Create(
                _avatar2Address,
                _signer2,
                0,
                tableSheets.GetAvatarSheets(),
                rankingMapAddress);
            avatar2State.worldInformation = new WorldInformation(
                0,
                tableSheets.WorldSheet,
                1);

            agent2State.avatarAddresses[0] = _avatar2Address;
#pragma warning disable CS0618
            // Use of obsolete method Currency.Legacy(): https://github.com/planetarium/lib9c/discussions/1319
            var currency = Currency.Legacy("NCG", 2, null);
#pragma warning restore CS0618
            var goldCurrencyState = new GoldCurrencyState(currency);
#pragma warning disable CS0618
            // Use of obsolete method Currency.Legacy(): https://github.com/planetarium/lib9c/discussions/1319
            _currency = Currency.Legacy("CRYSTAL", 18, null);
#pragma warning restore CS0618

            _state = _state
                .SetAgentState(_signer, agentState)
                .SetAvatarState(_avatarAddress, avatarState)
                .SetAgentState(_signer2, agent2State)
                .SetAvatarState(_avatar2Address, avatar2State)
                .SetLegacyState(gameConfigState.address, gameConfigState.Serialize())
                .SetLegacyState(Addresses.GoldCurrency, goldCurrencyState.Serialize());

            foreach ((var key, var value) in sheets)
            {
                _state = _state
                    .SetLegacyState(Addresses.TableSheet.Derive(key), value.Serialize());
            }
        }

        public (List<Guid> Equipments, List<Guid> Costumes) GetDummyItems(AvatarState avatarState)
        {
            var items = Doomfist.GetAllParts(_tableSheets, avatarState.level);
            foreach (var equipment in items)
            {
                avatarState.inventory.AddItem(equipment);
            }

            var equipments = items.Select(e => e.NonFungibleId).ToList();

            var random = new TestRandom();
            var costumes = new List<Guid>();
            if (avatarState.level >= GameConfig.RequireCharacterLevel.CharacterFullCostumeSlot)
            {
                var costumeId = _tableSheets
                    .CostumeItemSheet
                    .Values
                    .First(r => r.ItemSubType == ItemSubType.FullCostume)
                    .Id;

                var costume = (Costume)ItemFactory.CreateItem(
                    _tableSheets.ItemSheet[costumeId], random);
                avatarState.inventory.AddItem(costume);
                costumes.Add(costume.ItemId);
            }

            return (equipments, costumes);
        }

        public AvatarState GetAvatarState(AvatarState avatarState, out List<Guid> equipments, out List<Guid> costumes)
        {
            avatarState.level = 999;
            (equipments, costumes) = GetDummyItems(avatarState);

            _state = _state.SetAvatarState(_avatarAddress, avatarState);

            return avatarState;
        }

        public AvatarState AddMedal(AvatarState avatarState, ArenaSheet.Row row, int count = 1)
        {
            var materialSheet = _state.GetSheet<MaterialItemSheet>();
            foreach (var data in row.Round)
            {
                if (!data.ArenaType.Equals(ArenaType.Season))
                {
                    continue;
                }

                var itemId = data.MedalId;
                var material = ItemFactory.CreateMaterial(materialSheet, itemId);
                avatarState.inventory.AddItem(material, count);
            }

            _state = _state.SetAvatarState(_avatarAddress, avatarState);

            return avatarState;
        }

        [Theory]
        [InlineData(0, 1, 1, "0")]
        [InlineData(4_479_999L, 1, 2, "998001")]
        [InlineData(4_480_001L, 1, 2, "998001")]
        [InlineData(100, 1, 8, "998001")]
        public void Execute(long blockIndex, int championshipId, int round, string balance)
        {
            var arenaSheet = _state.GetSheet<ArenaSheet>();
            if (!arenaSheet.TryGetValue(championshipId, out var row))
            {
                throw new SheetRowNotFoundException(
                    nameof(ArenaSheet), $"championship Id : {championshipId}");
            }

            var avatarState = _state.GetAvatarState(_avatarAddress);
            avatarState = GetAvatarState(avatarState, out var equipments, out var costumes);
            avatarState = AddMedal(avatarState, row, 80);

            var context = new ActionContext();
            var state = balance == "0"
                ? _state
                : _state.MintAsset(context, _signer, FungibleAssetValue.Parse(_currency, balance));

            var action = new JoinArena3()
            {
                championshipId = championshipId,
                round = round,
                costumes = costumes,
                equipments = equipments,
                runeInfos = new List<RuneSlotInfo>(),
                avatarAddress = _avatarAddress,
            };

            state = action.Execute(new ActionContext
            {
                PreviousState = state,
                Signer = _signer,
                RandomSeed = _random.Seed,
                BlockIndex = blockIndex,
            });

            // ArenaParticipants
            var arenaParticipantsAdr = ArenaParticipants.DeriveAddress(championshipId, round);
            var serializedArenaParticipants = (List)state.GetLegacyState(arenaParticipantsAdr);
            var arenaParticipants = new ArenaParticipants(serializedArenaParticipants);

            Assert.Equal(arenaParticipantsAdr, arenaParticipants.Address);
            Assert.Equal(_avatarAddress, arenaParticipants.AvatarAddresses.First());

            // ArenaAvatarState
            var arenaAvatarStateAdr = ArenaAvatarState.DeriveAddress(_avatarAddress);
            var serializedArenaAvatarState = (List)state.GetLegacyState(arenaAvatarStateAdr);
            var arenaAvatarState = new ArenaAvatarState(serializedArenaAvatarState);

            foreach (var guid in arenaAvatarState.Equipments)
            {
                Assert.Contains(avatarState.inventory.Equipments, x => x.ItemId.Equals(guid));
            }

            foreach (var guid in arenaAvatarState.Costumes)
            {
                Assert.Contains(avatarState.inventory.Costumes, x => x.ItemId.Equals(guid));
            }

            Assert.Equal(arenaAvatarStateAdr, arenaAvatarState.Address);

            // ArenaScore
            var arenaScoreAdr = ArenaScore.DeriveAddress(_avatarAddress, championshipId, round);
            var serializedArenaScore = (List)state.GetLegacyState(arenaScoreAdr);
            var arenaScore = new ArenaScore(serializedArenaScore);

            Assert.Equal(arenaScoreAdr, arenaScore.Address);
            Assert.Equal(GameConfig.ArenaScoreDefault, arenaScore.Score);

            // ArenaInformation
            var arenaInformationAdr = ArenaInformation.DeriveAddress(_avatarAddress, championshipId, round);
            var serializedArenaInformation = (List)state.GetLegacyState(arenaInformationAdr);
            var arenaInformation = new ArenaInformation(serializedArenaInformation);

            Assert.Equal(arenaInformationAdr, arenaInformation.Address);
            Assert.Equal(0, arenaInformation.Win);
            Assert.Equal(0, arenaInformation.Lose);
            Assert.Equal(ArenaInformation.MaxTicketCount, arenaInformation.Ticket);

            if (!row.TryGetRound(round, out var roundData))
            {
                throw new RoundNotFoundException($"{nameof(JoinArena3)} : {row.ChampionshipId} / {round}");
            }

            Assert.Equal(0 * _currency, state.GetBalance(_signer, _currency));
        }

        [Theory]
        [InlineData(9999)]
        public void Execute_SheetRowNotFoundException(int championshipId)
        {
            var avatarState = _state.GetAvatarState(_avatarAddress);
            avatarState = GetAvatarState(avatarState, out var equipments, out var costumes);
            var state = _state.SetAvatarState(_avatarAddress, avatarState);

            var action = new JoinArena3()
            {
                championshipId = championshipId,
                round = 1,
                costumes = costumes,
                equipments = equipments,
                runeInfos = new List<RuneSlotInfo>(),
                avatarAddress = _avatarAddress,
            };

            Assert.Throws<SheetRowNotFoundException>(() => action.Execute(new ActionContext()
            {
                PreviousState = state,
                Signer = _signer,
                RandomSeed = 0,
            }));
        }

        [Theory]
        [InlineData(123)]
        public void Execute_RoundNotFoundByIdsException(int round)
        {
            var avatarState = _state.GetAvatarState(_avatarAddress);
            avatarState = GetAvatarState(avatarState, out var equipments, out var costumes);
            var state = _state.SetAvatarState(_avatarAddress, avatarState);

            var action = new JoinArena3()
            {
                championshipId = 1,
                round = round,
                costumes = costumes,
                equipments = equipments,
                runeInfos = new List<RuneSlotInfo>(),
                avatarAddress = _avatarAddress,
            };

            Assert.Throws<RoundNotFoundException>(() => action.Execute(new ActionContext()
            {
                PreviousState = state,
                Signer = _signer,
                RandomSeed = 0,
                BlockIndex = 1,
            }));
        }

        [Theory]
        [InlineData(8)]
        public void Execute_NotEnoughMedalException(int round)
        {
            var avatarState = _state.GetAvatarState(_avatarAddress);
            GetAvatarState(avatarState, out var equipments, out var costumes);
            var preCurrency = 99800100000 * _currency;
            var context = new ActionContext();
            var state = _state.MintAsset(context, _signer, preCurrency);

            var action = new JoinArena3()
            {
                championshipId = 1,
                round = round,
                costumes = costumes,
                equipments = equipments,
                runeInfos = new List<RuneSlotInfo>(),
                avatarAddress = _avatarAddress,
            };

            Assert.Throws<NotEnoughMedalException>(() => action.Execute(new ActionContext()
            {
                PreviousState = state,
                Signer = _signer,
                RandomSeed = 0,
                BlockIndex = 100,
            }));
        }

        [Theory]
        [InlineData(6, 0)] // discounted_entrance_fee
        [InlineData(8, 100)] // entrance_fee
        public void Execute_NotEnoughFungibleAssetValueException(int round, long blockIndex)
        {
            var avatarState = _state.GetAvatarState(_avatarAddress);
            GetAvatarState(avatarState, out var equipments, out var costumes);
            var state = _state.SetAvatarState(_avatarAddress, avatarState);

            var action = new JoinArena3()
            {
                championshipId = 1,
                round = round,
                costumes = costumes,
                equipments = equipments,
                runeInfos = new List<RuneSlotInfo>(),
                avatarAddress = _avatarAddress,
            };

            Assert.Throws<NotEnoughFungibleAssetValueException>(() => action.Execute(new ActionContext()
            {
                PreviousState = state,
                Signer = _signer,
                RandomSeed = 0,
                BlockIndex = blockIndex,
            }));
        }

        [Fact]
        public void Execute_ArenaScoreAlreadyContainsException()
        {
            var avatarState = _state.GetAvatarState(_avatarAddress);
            avatarState = GetAvatarState(avatarState, out var equipments, out var costumes);
            var state = _state.SetAvatarState(_avatarAddress, avatarState);

            var action = new JoinArena3()
            {
                championshipId = 1,
                round = 1,
                costumes = costumes,
                equipments = equipments,
                runeInfos = new List<RuneSlotInfo>(),
                avatarAddress = _avatarAddress,
            };

            state = action.Execute(new ActionContext
            {
                PreviousState = state,
                Signer = _signer,
                RandomSeed = _random.Seed,
                BlockIndex = 1,
            });

            Assert.Throws<ArenaScoreAlreadyContainsException>(() => action.Execute(new ActionContext()
            {
                PreviousState = state,
                Signer = _signer,
                RandomSeed = 0,
                BlockIndex = 2,
            }));
        }

        [Fact]
        public void Execute_ArenaScoreAlreadyContainsException2()
        {
            const int championshipId = 1;
            const int round = 1;

            var avatarState = _state.GetAvatarState(_avatarAddress);
            avatarState = GetAvatarState(avatarState, out var equipments, out var costumes);
            var state = _state.SetAvatarState(_avatarAddress, avatarState);

            var arenaScoreAdr = ArenaScore.DeriveAddress(_avatarAddress, championshipId, round);
            var arenaScore = new ArenaScore(_avatarAddress, championshipId, round);
            state = state.SetLegacyState(arenaScoreAdr, arenaScore.Serialize());

            var action = new JoinArena3()
            {
                championshipId = championshipId,
                round = round,
                costumes = costumes,
                equipments = equipments,
                runeInfos = new List<RuneSlotInfo>(),
                avatarAddress = _avatarAddress,
            };

            Assert.Throws<ArenaScoreAlreadyContainsException>(() => action.Execute(new ActionContext()
            {
                PreviousState = state,
                Signer = _signer,
                RandomSeed = 0,
                BlockIndex = 1,
            }));
        }

        [Fact]
        public void Execute_ArenaInformationAlreadyContainsException()
        {
            const int championshipId = 1;
            const int round = 1;

            var avatarState = _state.GetAvatarState(_avatarAddress);
            avatarState = GetAvatarState(avatarState, out var equipments, out var costumes);
            var state = _state.SetAvatarState(_avatarAddress, avatarState);

            var arenaInformationAdr = ArenaInformation.DeriveAddress(_avatarAddress, championshipId, round);
            var arenaInformation = new ArenaInformation(_avatarAddress, championshipId, round);
            state = state.SetLegacyState(arenaInformationAdr, arenaInformation.Serialize());

            var action = new JoinArena3()
            {
                championshipId = championshipId,
                round = round,
                costumes = costumes,
                equipments = equipments,
                runeInfos = new List<RuneSlotInfo>(),
                avatarAddress = _avatarAddress,
            };

            Assert.Throws<ArenaInformationAlreadyContainsException>(() => action.Execute(new ActionContext()
            {
                PreviousState = state,
                Signer = _signer,
                RandomSeed = 0,
                BlockIndex = 1,
            }));
        }

        [Fact]
        public void Execute_NotEnoughClearedStageLevelException()
        {
            var action = new JoinArena3()
            {
                championshipId = 1,
                round = 1,
                costumes = new List<Guid>(),
                equipments = new List<Guid>(),
                runeInfos = new List<RuneSlotInfo>(),
                avatarAddress = _avatar2Address,
            };

            Assert.Throws<NotEnoughClearedStageLevelException>(() => action.Execute(new ActionContext()
            {
                PreviousState = _state,
                Signer = _signer2,
                RandomSeed = 0,
            }));
        }

        [Theory]
        [InlineData(0, 30001, 1, 30001, typeof(DuplicatedRuneIdException))]
        [InlineData(1, 10002, 1, 30001, typeof(DuplicatedRuneSlotIndexException))]
        public void ExecuteDuplicatedException(int slotIndex, int runeId, int slotIndex2, int runeId2, Type exception)
        {
            var championshipId = 1;
            var round = 1;
            var arenaSheet = _state.GetSheet<ArenaSheet>();
            if (!arenaSheet.TryGetValue(championshipId, out var row))
            {
                throw new SheetRowNotFoundException(
                    nameof(ArenaSheet), $"championship Id : {championshipId}");
            }

            var avatarState = _state.GetAvatarState(_avatarAddress);
            avatarState = GetAvatarState(avatarState, out var equipments, out var costumes);
            avatarState = AddMedal(avatarState, row, 80);

            var context = new ActionContext();
            var ncgCurrency = _state.GetGoldCurrency();
            var state = _state.MintAsset(context, _signer, 99999 * ncgCurrency);

            var unlockRuneSlot = new UnlockRuneSlot()
            {
                AvatarAddress = _avatarAddress,
                SlotIndex = 1,
            };

            state = unlockRuneSlot.Execute(new ActionContext
            {
                BlockIndex = 1,
                PreviousState = state,
                Signer = _signer,
                RandomSeed = 0,
            });

            var action = new JoinArena3()
            {
                championshipId = championshipId,
                round = round,
                costumes = costumes,
                equipments = equipments,
                runeInfos = new List<RuneSlotInfo>()
                {
                    new (slotIndex, runeId),
                    new (slotIndex2, runeId2),
                },
                avatarAddress = _avatarAddress,
            };

            Assert.Throws(exception, () => action.Execute(new ActionContext
            {
                PreviousState = state,
                Signer = _signer,
                RandomSeed = 0,
            }));
        }
    }
}
