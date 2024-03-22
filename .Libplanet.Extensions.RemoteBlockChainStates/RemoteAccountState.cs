using System.Security.Cryptography;
using Bencodex;
using Bencodex.Types;
using GraphQL;
using GraphQL.Client.Http;
using GraphQL.Client.Serializer.SystemTextJson;
using Libplanet.Action.State;
using Libplanet.Common;
using Libplanet.Crypto;
using Libplanet.Store.Trie;
using Libplanet.Types.Blocks;

namespace Libplanet.Extensions.RemoteBlockChainStates;

public class RemoteAccountState : IAccountState
{
    private readonly Uri _explorerEndpoint;
    private readonly GraphQLHttpClient _graphQlHttpClient;

    public RemoteAccountState(
        Uri explorerEndpoint,
        BlockHash? offsetBlockHash,
        Address? address)
    {
        _explorerEndpoint = explorerEndpoint;
        _graphQlHttpClient =
            new GraphQLHttpClient(_explorerEndpoint, new SystemTextJsonSerializer());
        var response = _graphQlHttpClient.SendQueryAsync<GetAccountStateResponseType>(
            new GraphQLRequest(
                @"query GetAccount($accountAddress: Address!, $offsetBlockHash: ID!)
                {
                    stateQuery
                    {
                        accountState(accountAddress: $accountAddress, offsetBlockHash: $offsetBlockHash)
                    }
                }",
                operationName: "GetAccount",
                variables: new
                {
                    offsetBlockHash = offsetBlockHash is { } hash
                        ? ByteUtil.Hex(hash.ByteArray)
                        : throw new NotSupportedException(),
                    accountAddress = address is { } addr
                        ? addr.ToString()
                        : throw new NotSupportedException(),
                })).Result;
        Trie = new HollowTrie(HashDigest<SHA256>.FromString(response.Data.StateQuery.AccountState.StateRootHash));
    }

    public RemoteAccountState(
        Uri explorerEndpoint,
        Address? address,
        HashDigest<SHA256>? offsetStateRootHash)
    {
        _explorerEndpoint = explorerEndpoint;
        _graphQlHttpClient =
            new GraphQLHttpClient(_explorerEndpoint, new SystemTextJsonSerializer());
        var response = _graphQlHttpClient.SendQueryAsync<GetAccountStateResponseType>(
            new GraphQLRequest(
                @"query GetAccount($accountAddress: Address!, $offsetStateRootHash: HashDigest_SHA256!)
                {
                    stateQuery
                    {
                        accountState(accountAddress: $accountAddress, offsetStateRootHash: $offsetStateRootHash)
                    }
                }",
                operationName: "GetAccount",
                variables: new
                {
                    accountAddress = address is { } addr
                        ? addr.ToString()
                        : throw new NotSupportedException(),
                    offsetStateRootHash = offsetStateRootHash is { } hash
                        ? ByteUtil.Hex(hash.ByteArray)
                        : throw new NotSupportedException(),
                })).Result;
        Trie = new HollowTrie(HashDigest<SHA256>.FromString(response.Data.StateQuery.AccountState.StateRootHash));
    }

    public RemoteAccountState(
        Uri explorerEndpoint,
        HashDigest<SHA256>? accountStateRootHash)
    {
        _explorerEndpoint = explorerEndpoint;
        _graphQlHttpClient =
            new GraphQLHttpClient(_explorerEndpoint, new SystemTextJsonSerializer());
        Trie = new HollowTrie(accountStateRootHash);
    }

    public ITrie Trie { get; }

    public IReadOnlyList<IValue?> GetStates(IReadOnlyList<Address> addresses)
        => addresses.Select(address => GetState(address)).ToList().AsReadOnly();

    public IValue? GetState(Address address)
    {
        var response = _graphQlHttpClient.SendQueryAsync<GetStatesResponseType>(
            new GraphQLRequest(
                @"query GetState(
                    $address: Address!,
                    $accountStateRootHash: HashDigest_SHA256!)
                {
                    stateQuery
                    {
                        states(
                            address: $address,
                            accountStateRootHash: $accountStateRootHash)
                    }
                }",
                operationName: "GetState",
                variables: new
                {
                    address,
                    accountStateRootHash = Trie.Hash is { } accountSrh
                        ? ByteUtil.Hex(accountSrh.ByteArray)
                        : null,
                })).Result;
        var codec = new Codec();
        return response.Data.StateQuery.States is { } state ? codec.Decode(state) : null;
    }

    private class GetAccountStateResponseType
    {
        public StateQueryWithAccountStateType StateQuery { get; set; }
    }

    private class StateQueryWithAccountStateType
    {
        public AccountStateType AccountState { get; set; }
    }

    public class AccountStateType
    {
        public string StateRootHash { get; set; }
    }

    private class GetStatesResponseType
    {
        public StateQueryWithStatesType StateQuery { get; set; }
    }

    private class StateQueryWithStatesType
    {
        public byte[] States { get; set; }
    }
}
