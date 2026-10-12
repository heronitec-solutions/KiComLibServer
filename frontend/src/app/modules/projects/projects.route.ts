import { ChangeDetectionStrategy, Component } from '@angular/core';
import { ProjectsModule } from './projects.module';

@Component({
    selector: 'project-table-route',
    imports: [ProjectsModule],
    template: '<project-table></project-table>',
    styles: [':host { display: contents; }'],
    changeDetection: ChangeDetectionStrategy.Eager
})
export class ProjectTableRouteComponent {}

@Component({
    selector: 'project-detail-route',
    imports: [ProjectsModule],
    template: '<project-detail></project-detail>',
    styles: [':host { display: contents; }'],
    changeDetection: ChangeDetectionStrategy.Eager
})
export class ProjectDetailRouteComponent {}

@Component({
    selector: 'visual-diff-route',
    imports: [ProjectsModule],
    template: '<visual-diff-viewer></visual-diff-viewer>',
    styles: [':host { display: contents; }'],
    changeDetection: ChangeDetectionStrategy.Eager
})
export class VisualDiffRouteComponent {}
