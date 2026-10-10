/** Module 6: one switch with its optional daily limit and what was spent through it today. */
export interface ChannelControl {
  enabled: boolean;
  /** null = no extra limit (only the available credit applies). */
  dailyLimit: number | null;
  spentToday: number;
}

export type ControlKey = 'pos' | 'online' | 'contactless' | 'atm' | 'international';

export interface CardControls extends Record<ControlKey, ChannelControl> {
  cardId: number;
  maskedCardNumber: string;
  cardStatus: string;
  isLocked: boolean;
  lockedAt: string | null;
  creditLimit: number;
  /** Bank rule (RBI tap-and-pay limit) - not changeable by the cardholder. */
  contactlessPerTransactionLimit: number;
  homeCountryCode: string;
  updatedAt: string;
}

export interface ChannelSettingRequest {
  enabled: boolean;
  dailyLimit: number | null;
}

export type UpdateCardControlsRequest = Record<ControlKey, ChannelSettingRequest>;
