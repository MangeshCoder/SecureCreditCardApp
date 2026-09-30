import { Component, OnInit, inject, signal } from '@angular/core';
import { CurrencyPipe, DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { environment } from '../../../../environments/environment';
import { Card } from '../../../core/models/card.models';
import { CardService } from '../../../core/services/card.service';
import { apiErrorMessages } from '../../../core/utils/api-error';

@Component({
  selector: 'app-all-cards',
  imports: [CurrencyPipe, DatePipe, FormsModule, RouterLink],
  templateUrl: './all-cards.html'
})
export class AllCards implements OnInit {
  private readonly cardService = inject(CardService);

  protected readonly currency = environment.currencyCode;
  protected readonly cards = signal<Card[]>([]);
  protected readonly errors = signal<string[]>([]);
  protected readonly editingLimit = signal<number | null>(null);
  protected newLimit = 0;

  ngOnInit(): void {
    this.cardService.getAllCards().subscribe({
      next: cards => this.cards.set(cards),
      error: e => this.errors.set(apiErrorMessages(e))
    });
  }

  block(card: Card): void {
    this.run(this.cardService.blockCard(card.cardId));
  }

  activate(card: Card): void {
    this.run(this.cardService.activateCard(card.cardId));
  }

  editLimit(card: Card): void {
    this.newLimit = card.creditLimit;
    this.editingLimit.set(card.cardId);
  }

  saveLimit(card: Card): void {
    this.run(this.cardService.updateCreditLimit(card.cardId, this.newLimit), () => this.editingLimit.set(null));
  }

  private run(request: ReturnType<CardService['blockCard']>, done?: () => void): void {
    this.errors.set([]);
    request.subscribe({
      next: updated => {
        this.cards.update(list => list.map(c => (c.cardId === updated.cardId ? updated : c)));
        done?.();
      },
      error: e => this.errors.set(apiErrorMessages(e))
    });
  }
}
