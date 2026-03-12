namespace Btfly.API.Models.Enums;

public enum ServerType { Dark = 0, Grey = 1, Light = 2 }

public enum AccountRole { User = 0, Moderator = 1, PlatformAdmin = 2 }

public enum NotificationType
{
    Like        = 0,  // Someone liked your post
    Reply       = 1,  // Someone replied to your post
    Follow      = 2,  // Someone followed you
    Mention     = 3,  // Someone mentioned you in a post
    Refly       = 4,  // Someone reflyed your post
}
