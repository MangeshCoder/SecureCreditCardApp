import { Component, OnInit, inject, signal } from '@angular/core';
import { CurrencyPipe, DatePipe } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { environment } from '../../../../environments/environment';
import { Cardholder, IssuedCardResponse } from '../../../core/models/card.models';
import { CardService } from '../../../core/services/card.service';
import { CardholderService } from '../../../core/services/cardholder.service';
import { apiErrorMessages } from '../../../core/utils/api-error';

@Component({
  selector: 'app-cardholders',
  imports: [CurrencyPipe, DatePipe, ReactiveFormsModule],
  templateUrl: './cardholders.html'
})
export class Cardholders implements OnInit {
  private readonly cardholderService = inject(CardholderService);
  private readonly cardService = inject(CardService);

  protected readonly currency = environment.currencyCode;
  protected readonly cardholders = signal<Cardholder[]>([]);
  protected readonly errors = signal<string[]>([]);
  protected readonly issuingFor = signal<Cardholder | null>(null);
  /** One-time view of the new card's secrets (like a PIN mailer). Cleared when dismissed. */
  protected readonly issued = signal<IssuedCardResponse | null>(null);

  protected readonly issueForm = inject(FormBuilder).nonNullable.group({
    creditLimit: [50000, [Validators.required, Validators.min(1), Validators.max(1_000_000)]]
  });

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.cardholderService.getAll().subscribe({
      next: list => this.cardholders.set(list),
      error: e => this.errors.set(apiErrorMessages(e))
    });
  }

  startIssue(cardholder: Cardholder): void {
    this.errors.set([]);
    this.issued.set(null);
    this.issueForm.reset({ creditLimit: 50000 });
    this.issuingFor.set(cardholder);
  }

  issue(): void {
    const cardholder = this.issuingFor();
    if (!cardholder || this.issueForm.invalid) {
      this.issueForm.markAllAsTouched();
      return;
    }
    this.cardService.issueCard({ cardholderId: cardholder.cardholderId, ...this.issueForm.getRawValue() }).subscribe({
      next: result => {
        this.issuingFor.set(null);
        this.issued.set(result);
        this.load();
      },
      error: e => this.errors.set(apiErrorMessages(e))
    });
  }

  toggleActive(c: Cardholder): void {
    this.cardholderService.setActive(c.cardholderId, !c.isActive).subscribe({
      next: () => this.load(),
      error: e => this.errors.set(apiErrorMessages(e))
    });
  }
}
