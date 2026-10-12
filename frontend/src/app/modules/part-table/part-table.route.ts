import { ChangeDetectionStrategy, Component } from '@angular/core';
import { PartTableModule } from './part-table.module';

@Component({
    selector: 'part-table-route',
    imports: [PartTableModule],
    template: '<part-table></part-table>',
    styles: [':host { display: contents; }'],
    changeDetection: ChangeDetectionStrategy.Eager
})
export class PartTableRouteComponent {}
