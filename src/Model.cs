using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace GPhotosAPI
{
    abstract class BaseNextPage
    {
        public string nextPageToken { get; set; }
    }
    public class SharedAlbumOptions
    {
        /// <summary>
        /// True if the shared album allows collaborators (users who have joined the album) to add media items to it. Defaults to false.
        /// </summary>
        public bool isCollaborative { get; set; }

        /// <summary>
        /// True if the shared album allows the owner and the collaborators (users who have joined the album) to add comments to the album. Defaults to false.
        /// </summary>
        public bool isCommentable { get; set; }
    }
    public class ShareInfo
    {
        /// <summary>
        /// Options that control the sharing of an album.
        /// </summary>
        public SharedAlbumOptions sharedAlbumOptions { get; set; } = default!;

        /// <summary>
        /// A link to the album that's now shared on the Google Photos website and app. Anyone with the link can access this shared album and see all of the items present in the album.
        /// </summary>
        public string shareableUrl { get; set; } = default!;

        /// <summary>
        /// A token that can be used by other users to join this shared album via the API.
        /// </summary>
        public string shareToken { get; set; } = default!;

        /// <summary>
        /// True if the user has joined the album. This is always true for the owner of the shared album.
        /// </summary>
        public bool isJoined { get; set; }

        /// <summary>
        /// True if the user owns the album.
        /// </summary>
        public bool isOwned { get; set; }
    }
    public class Album
    {
        /// <summary>
        /// Identifier for the album. This is a persistent identifier that can be used between sessions to identify this album.
        /// </summary>
        public string id { get; set; } = default!;

        /// <summary>
        /// Name of the album displayed to the user in their Google Photos account. This string shouldn't be more than 500 characters.
        /// </summary>
        public string title { get; set; } = default!;

        /// <summary>
        /// [Output only] Google Photos URL for the album. The user needs to be signed in to their Google Photos account to access this link.
        /// </summary>
        public string productUrl { get; set; } = default!;

        /// <summary>
        /// [Output only] A URL to the cover photo's bytes. This shouldn't be used as is. Parameters should be appended to this URL before use. See the developer documentation for a complete list of supported parameters. For example, '=w2048-h1024' sets the dimensions of the cover photo to have a width of 2048 px and height of 1024 px.
        /// </summary>
        public string coverPhotoBaseUrl { get; set; } = default!;

        /// <summary>
        /// [Output only] Identifier for the media item associated with the cover photo.
        /// </summary>
        public string coverPhotoMediaItemId { get; set; } = default!;

        /// <summary>
        /// [Output only] True if you can create media items in this album. This field is based on the scopes granted and permissions of the album. If the scopes are changed or permissions of the album are changed, this field is updated.
        /// </summary>
        public bool isWriteable { get; set; }

        /// <summary>
        /// [Output only] The number of media items in the album.
        /// </summary>
        public int mediaItemsCount { get; set; }

        /// <summary>
        /// [Output only] Information related to shared albums.This field is only populated if the album is a shared album, the developer created the album and the user has granted the photoslibrary.sharing scope.
        /// </summary>
        public ShareInfo shareInfo { get; set; }

        public override string ToString()
        {
            return $"{title}, {mediaItemsCount} media items";
        }
    }
    class AlbumsResponse : BaseNextPage
    {
        public List<Album> albums { get; set; }
        public List<Album> sharedAlbums { get; set; }
    }

    abstract class Camera
    {
        public string? cameraMake { get; set; }
        public string? cameraModel { get; set; }
    }
    class Photo : Camera
    {
        public float focalLength { get; set; }
        public float apertureFNumber { get; set; }
        public int isoEquivalent { get; set; }
        public string exposureTime { get; set; }
    }

    public class ContributorInfo
    {
        public string profilePictureBaseUrl { get; set; }
        public string displayName { get; set; }
    }

    class Video : Camera
    {
        public float fps { get; set; }
        public string status { get; set; } = default!;
    }
    class MediaMetaData
    {
        public DateTime creationTime { get; set; }
        public int width { get; set; } = default!;
        public int height { get; set; } = default!;
        public Photo photo { get; set; }
        public Video video { get; set; }
    }
    //https://developers.google.com/photos/library/guides/access-media-items#media-items
    class MediaItem
    {
        /// <summary>
        /// A permanent, stable ID used to identify the object.
        /// </summary>
        public string id { get; set; } = default!;

        /// <summary>
        /// Description of the media item as seen inside Google Photos.
        /// </summary>
        public string description { get; set; }

        /// <summary>
        /// A link to the image inside Google Photos. This link can't be opened by the developer, only by the user.
        /// </summary>
        public string productUrl { get; set; } = default!;

        /// <summary>
        /// Used to access the raw bytes. For more information, see Base URLs.
        /// </summary>
        public string baseUrl { get; set; } = default!;

        /// <summary>
        /// Used to help determine if the media item is more than 1 hour old, if so then the baseUrl is expired.
        /// </summary>
        //[JsonIgnore]
        //public DateTime syncDate { get; } = DateTime.UtcNow;

        //[JsonIgnore]
        //public bool isPhoto { get { return mediaMetadata.photo is object; } }

        //[JsonIgnore]
        //public bool isVideo { get { return mediaMetadata.video is object; } }

        /// <summary>
        /// The type of the media item to help easily identify the type of media (for example: image/jpg).
        /// </summary>
        public string mimeType { get; set; } = default!;//todo: nullability look further into this (will it return a mime type if we don't send one in?)

        /// <summary>
        /// Varies depending on the underlying type of the media, such as, photo or video. To reduce the payload, field masks can be used.
        /// </summary>
        public MediaMetaData mediaMetadata { get; set; } = default!;

        /// <summary>
        /// This field is only populated if the media item is in a shared album created by this app and the user has granted the .sharing scope.
        ///
        /// Contains information about the contributor who added this media item. For more details, see Share media.
        /// </summary>
        public ContributorInfo contributorInfo { get; set; }

        /// <summary>
        /// The filename of the media item shown to the user in the Google Photos app (within the item's info section).
        /// </summary>
        public string filename { get; set; } = default!;

        public override string ToString() => JsonConvert.SerializeObject(this);
    }
    class MediaItemsResponse : BaseNextPage
    {
        public List<MediaItem> mediaItems { get; set; } = default!;
    }
}
