export type NotificationCategory = 'Transaction' | 'Card' | 'Security';

/** Module 7: an alert in the in-app inbox (also sent by SMS and e-mail). */
export interface AppNotification {
  notificationId: number;
  cardId: number | null;
  category: NotificationCategory;
  title: string;
  message: string;
  createdAt: string;
  isRead: boolean;
  deliveryStatus: 'Pending' | 'Sent' | 'Failed';
}

export interface UnreadCount {
  count: number;
}
