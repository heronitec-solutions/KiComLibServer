import { Component, Input, ChangeDetectionStrategy } from '@angular/core';
import { RouterOutlet } from '@angular/router';

@Component({
    selector: 'content-area',
    imports: [
        RouterOutlet
    ],
    templateUrl: './content-area.component.html',
    changeDetection: ChangeDetectionStrategy.Eager,
    styleUrl: './content-area.component.scss'
})
export class ContentAreaComponent {
  @Input()
  currentPage : string = 'welcome';
}
