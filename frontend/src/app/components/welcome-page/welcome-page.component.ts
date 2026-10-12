import { Component, OnInit, ChangeDetectionStrategy } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { RouterLink } from '@angular/router';
import { AppHeaderComponent } from '../app-header/app-header.component';
import { Part } from '../../modules/part-table/types/part';
import { PartService } from '../../modules/part-table/services/part.service';

@Component({
    selector: 'welcome-page',
    imports: [
        AppHeaderComponent,
        MatIconModule,
        RouterLink
    ],
    templateUrl: './welcome-page.component.html',
    changeDetection: ChangeDetectionStrategy.Eager,
    styleUrl: './welcome-page.component.scss'
})
export class WelcomePageComponent implements OnInit {

  totalParts = 0;
  activeParts = 0;
  endOfLifeParts = 0;
  partsWithoutSymbol = 0;

  constructor(private partService: PartService) { }

  ngOnInit(): void {
    this.partService.getParts({ active: 'id', direction: 'asc' }).subscribe({
      next: parts => this.updateSummary(parts),
      error: error => console.error('Error:', error)
    });
  }

  private updateSummary(parts: Part[]) {
    this.totalParts = parts.length;
    this.endOfLifeParts = parts.filter(part => part.lifecycleStatus === 'eol').length;
    this.activeParts = parts.filter(part => part.lifecycleStatus === 'active').length;
    this.partsWithoutSymbol = parts.filter(part => !part.symbolId).length;
  }
}
