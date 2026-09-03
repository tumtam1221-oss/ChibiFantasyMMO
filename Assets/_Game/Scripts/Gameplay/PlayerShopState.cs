using System.Collections.Generic;
using ChibiFantasy.Core;

namespace ChibiFantasy.Gameplay
{
    /// <summary>Where a listing stands.</summary>
    /// <remarks>
    /// One-way, and terminal states are final. That is what makes "a listing cannot sell
    /// twice" structural: a sold listing cannot go back to active, so a second purchase
    /// finds nothing to buy.
    /// </remarks>
    public enum ShopListingState
    {
        /// <summary>On sale.</summary>
        Active = 0,

        /// <summary>Bought. Terminal.</summary>
        Sold = 1,

        /// <summary>Withdrawn by the seller. Terminal.</summary>
        Cancelled = 2
    }

    /// <summary>Whether a shop is trading.</summary>
    public enum PlayerShopState_Status
    {
        Open = 0,
        Closed = 1,

        /// <summary>Taken down. Terminal.</summary>
        Removed = 2
    }

    /// <summary>
    /// A place in the world, without an engine type.
    /// </summary>
    /// <remarks>
    /// Three floats and a map reference. A <c>Transform</c> or a <c>Vector3</c> here would
    /// put <c>UnityEngine</c> into the engine-free assembly, and a shop's whereabouts has to
    /// persist as columns rather than as a scene object.
    ///
    /// Deliberately not <c>CombatPosition</c>: that names a place in a fight and carries no
    /// map, and a shop that moved between maps without changing its coordinates would be
    /// somewhere else entirely.
    /// </remarks>
    public readonly struct WorldPlacement
    {
        public WorldPlacement(DefinitionId map, float x, float y, float z, float facingDegrees = 0f)
        {
            Map = map;
            X = x;
            Y = y;
            Z = z;
            FacingDegrees = facingDegrees;
        }

        /// <summary>Reference to a <see cref="Data.MapDefinition"/>.</summary>
        public DefinitionId Map { get; }

        public float X { get; }

        public float Y { get; }

        public float Z { get; }

        public float FacingDegrees { get; }

        public bool IsValid => Map.IsValid;

        public override string ToString()
        {
            return Map + " (" + X + ", " + Y + ", " + Z + ")";
        }
    }

    /// <summary>
    /// One item a player has put up for sale.
    /// </summary>
    /// <remarks>
    /// <b>It holds the item, it does not describe it.</b> The listed object is moved out of
    /// the seller's bag and held here in escrow -- see <see cref="Escrow"/>. That is what
    /// makes every anti-duplication invariant true by construction rather than by checking:
    /// while listed, the item is in exactly one place, it is in no container, so it cannot be
    /// equipped, consumed, socketed, traded or listed again.
    ///
    /// The alternative -- leaving it in the bag with a flag -- needs every other system to
    /// remember to check the flag, and the first one that forgets duplicates an item.
    ///
    /// Flat because it has to persist: one row of a future <c>player_shop_listing</c> table
    /// is a listing id, a shop, a seller, an item instance, a definition, a quantity, a
    /// currency, a price and a state. The escrowed object itself is the ordinary item row it
    /// always was, with its owner unchanged until it sells.
    /// </remarks>
    public sealed class PlayerShopListing : IPersistentState
    {
        private ShopListingState _state = ShopListingState.Active;
        private Revision _revision;

        public PlayerShopListing(InstanceId listingId, InstanceId shop, CharacterId seller,
            OwnerId sellerOwner, Data.GameInstance escrow, int quantity, DefinitionId currency,
            int unitPrice, long createdTicks = 0L)
        {
            ListingId = listingId;
            Shop = shop;
            Seller = seller;
            SellerOwner = sellerOwner;
            Escrow = escrow;
            Quantity = quantity;
            Currency = currency;
            UnitPrice = unitPrice;
            CreatedTicks = createdTicks;
            _revision = Revision.Initial;
        }

        public InstanceId ListingId { get; }

        public InstanceId Shop { get; }

        public CharacterId Seller { get; }

        public OwnerId SellerOwner { get; }

        /// <summary>
        /// The exact object on sale, held out of any container.
        /// </summary>
        /// <remarks>Never cloned. The buyer receives this object, with its identity intact,
        /// which is what lets an ownership history follow one item across many sales.</remarks>
        public Data.GameInstance Escrow { get; }

        public InstanceId Item => Escrow == null ? InstanceId.None : Escrow.InstanceId;

        public DefinitionId ItemDefinition =>
            Escrow == null ? DefinitionId.None : Escrow.DefinitionId;

        public int Quantity { get; }

        /// <summary>Reference to a <see cref="Data.CurrencyDefinition"/>.</summary>
        public DefinitionId Currency { get; }

        /// <summary>Price for the whole listing. Integer, always.</summary>
        public int UnitPrice { get; }

        public long CreatedTicks { get; }

        public ShopListingState State => _state;

        public Revision Revision => _revision;

        public bool IsActive => _state == ShopListingState.Active;

        /// <summary>
        /// Settles the listing.
        /// </summary>
        /// <remarks>Refuses to move a settled listing, so a purchase that arrives twice finds
        /// it already sold and buys nothing. That refusal is the anti-duplication guarantee,
        /// not a convenience.</remarks>
        public bool TrySetState(ShopListingState state)
        {
            if (state == ShopListingState.Active || _state != ShopListingState.Active)
            {
                return false;
            }

            _state = state;
            _revision = _revision.Next();
            return true;
        }
    }

    /// <summary>
    /// One player's shop.
    /// </summary>
    /// <remarks>
    /// <b>Not an NPC shop.</b> Phase 11's <c>ShopDefinition</c> is authored content with
    /// authored prices, owned by nobody; this is runtime state owned by a character, whose
    /// stock is real items they own and whose prices they set. The two share no type and no
    /// service, so neither can accidentally become the other.
    ///
    /// <b>Its position is data.</b> <see cref="Placement"/> is a map reference and three
    /// floats, so a shop persists as columns and gameplay stays engine-free.
    ///
    /// Flat: one row of a future <c>player_shop</c> table plus its listings.
    /// </remarks>
    public sealed class PlayerShop : IPersistentState
    {
        private readonly List<PlayerShopListing> _listings = new List<PlayerShopListing>();

        private PlayerShopState_Status _status = PlayerShopState_Status.Open;
        private string _name;
        private Revision _revision;

        public PlayerShop(InstanceId shopId, CharacterId owner, OwnerId ownerId, string name,
            WorldPlacement placement, long createdTicks = 0L)
        {
            ShopId = shopId;
            Owner = owner;
            OwnerId = ownerId;
            _name = name;
            Placement = placement;
            CreatedTicks = createdTicks;
            _revision = Revision.Initial;
        }

        public InstanceId ShopId { get; }

        public CharacterId Owner { get; }

        public OwnerId OwnerId { get; }

        /// <summary>Display only. Never an identity.</summary>
        public string Name => _name;

        public WorldPlacement Placement { get; }

        public long CreatedTicks { get; }

        public PlayerShopState_Status Status => _status;

        public Revision Revision => _revision;

        public bool IsOpen => _status == PlayerShopState_Status.Open;

        public IReadOnlyList<PlayerShopListing> Listings => _listings;

        public int ListingCount => _listings.Count;

        /// <summary>How many listings are still on sale.</summary>
        public int ActiveListingCount
        {
            get
            {
                int count = 0;

                for (int i = 0; i < _listings.Count; i++)
                {
                    if (_listings[i].IsActive) count++;
                }

                return count;
            }
        }

        public PlayerShopListing FindListing(InstanceId listingId)
        {
            for (int i = 0; i < _listings.Count; i++)
            {
                if (_listings[i].ListingId == listingId) return _listings[i];
            }

            return null;
        }

        /// <summary>Whether this exact item is already on sale here.</summary>
        public bool HasActiveListingFor(InstanceId item)
        {
            for (int i = 0; i < _listings.Count; i++)
            {
                if (_listings[i].IsActive && _listings[i].Item == item) return true;
            }

            return false;
        }

        /// <summary>Adds a listing. Assignment only; every rule is <see cref="PlayerShopService"/>'s.</summary>
        public bool TryAddListing(PlayerShopListing listing)
        {
            if (listing == null || listing.Shop != ShopId) return false;
            if (HasActiveListingFor(listing.Item)) return false;

            _listings.Add(listing);
            _revision = _revision.Next();
            return true;
        }

        public bool TrySetStatus(PlayerShopState_Status status)
        {
            if (_status == PlayerShopState_Status.Removed || _status == status) return false;

            _status = status;
            _revision = _revision.Next();
            return true;
        }

        public bool TryRename(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || string.Equals(_name, name)) return false;

            _name = name;
            _revision = _revision.Next();
            return true;
        }
    }
}
