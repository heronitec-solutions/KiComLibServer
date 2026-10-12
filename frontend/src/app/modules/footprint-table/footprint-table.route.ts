import { ChangeDetectionStrategy, Component } from '@angular/core';
import { FootprintTableModule } from './footprint-table.module';

@Component({
    selector: 'footprint-table-route',
    imports: [FootprintTableModule],
    template: '<footprint-table></footprint-table>',
    styles: [':host { display: contents; }'],
    changeDetection: ChangeDetectionStrategy.Eager
})
export class FootprintTableRouteComponent {}
