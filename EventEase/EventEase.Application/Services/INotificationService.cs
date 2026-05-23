using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using EventEase.Core.Entities;

namespace EventEase.Application.Services
{
    public interface INotificationService
    {
        Task<List<Notification>> GetNotificationsAsync(Guid userId);
        Task<int> MarkAllAsReadAsync(Guid userId);
        Task<bool> DeleteNotificationAsync(Guid id, Guid userId);
        Task<int> ClearAllNotificationsAsync(Guid userId);
    }
}
