import { Transaction } from '../../core/models/transaction.models';

/** Bootstrap badge class for a ledger row's status. */
export function statusBadge(t: Transaction): string {
  switch (t.transactionStatus) {
    case 'Completed': return 'bg-success';
    case 'Declined': return 'bg-danger';
    default: return 'bg-secondary';
  }
}

/** Swipes take money off the card (shown negative); loads and refunds add money. */
export function signedAmount(t: Transaction): number {
  return t.transactionType === 'Swipe' ? -t.amount : t.amount;
}
